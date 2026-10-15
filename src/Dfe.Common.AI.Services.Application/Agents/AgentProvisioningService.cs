using System.Diagnostics;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.QualityGate.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.Options;
using GovUK.Dfe.AI.Agents.Guardrails.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.Agents;

public sealed class AgentProvisioningService(
    IAgentService agents,
    IAgentReleaseGate releaseGate,
    IFoundryGuardrailsService guardrails,
    GuardrailSettings guardrailSettings,
    ILogger<AgentProvisioningService> logger) : IAgentProvisioningService
{
    public async Task<IReadOnlyList<AgentReference>> ProvisionAsync(CancellationToken cancellationToken = default)
    {
        var definitions = CommonAgents.All;

        // The guardrail goes on first, so the release checks test the agents behind it, as they'll run in production.
        await CheckGuardrailAsync(cancellationToken);

        logger.LogInformation(Messages.Log.ReleaseChecksStarting, definitions.Length, string.Join(", ", definitions.Select(a => a.Name)));
        var gate = await releaseGate.EvaluateAsync(definitions, cancellationToken);
        var failed = gate.Where(r => !r.Passed).ToList();
        if (failed.Count != 0)
        {
            logger.LogError(Messages.Log.ReleaseChecksFailed, failed.Count, string.Join(", ", failed.Select(r => r.AgentName)));
            throw new AgentReleaseBlockedException(failed);
        }

        // The library provisions the agents it's given one after another, so each agent gets its own call and they
        // run side by side. The library logs each agent's name and version as it's provisioned.
        logger.LogInformation(Messages.Log.Provisioning);
        var started = Stopwatch.GetTimestamp();
        var provisioning = definitions.Select(definition => agents.ProvisionAsync([definition], cancellationToken)).ToArray();
        try
        {
            await Task.WhenAll(provisioning);
        }
        catch (Exception ex)
        {
            ThrowIfPartlyProvisioned(definitions, provisioning, ex);
            throw;
        }

        logger.LogInformation(Messages.Log.ProvisioningFinished, definitions.Length, Stopwatch.GetElapsedTime(started).TotalSeconds);
        return [.. provisioning.SelectMany(task => task.Result)];
    }

    /// <summary>
    /// Agents are provisioned side by side, so one can fail after others have new versions. Those versions must be
    /// reported, as no consuming app knows about them. If none were provisioned, Foundry is unchanged and the original
    /// failure is enough.
    /// </summary>
    private static void ThrowIfPartlyProvisioned(AgentDefinition[] definitions,
        Task<IReadOnlyList<AgentReference>>[] provisioning, Exception failure)
    {
        var provisioned = provisioning.Where(task => task.IsCompletedSuccessfully).SelectMany(task => task.Result).ToList();
        if (provisioned.Count == 0)
        {
            return;
        }

        var failed = definitions.Where((_, index) => !provisioning[index].IsCompletedSuccessfully).Select(d => d.Name).ToList();
        throw new AgentProvisioningIncompleteException(provisioned, failed, failure);
    }

    /// <summary>
    /// Confirms the Foundry guardrail, created in the Foundry portal, exists with its blocklists and is at least as strict
    /// as the settings. The job never creates or changes it, so it needs only Reader on the Foundry resource. The library
    /// puts the guardrail on every agent version it creates.
    /// </summary>
    private async Task CheckGuardrailAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(Messages.Log.CheckingGuardrail, guardrailSettings.Name, guardrailSettings.Targets);

        var report = await guardrails.CheckAsync(cancellationToken);
        if (!report.Passed)
        {
            throw new GuardrailNotInPlaceException(report);
        }

        logger.LogInformation(Messages.Log.GuardrailInPlace, report.Guardrail);
    }
}
