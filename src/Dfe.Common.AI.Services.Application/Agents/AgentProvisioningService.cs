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
        await ApplyGuardrailsAsync(cancellationToken);

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
        var provisioned = await Task.WhenAll(definitions.Select(definition =>
            agents.ProvisionAsync([definition], cancellationToken)));

        logger.LogInformation(Messages.Log.ProvisioningFinished, definitions.Length, Stopwatch.GetElapsedTime(started).TotalSeconds);
        return [.. provisioned.SelectMany(references => references)];
    }

    /// <summary>
    /// Creates or updates the Foundry guardrail and assigns it to every model deployment, so harmful content, jailbreaks,
    /// instructions hidden in evidence and protected material are blocked before the model answers.
    /// </summary>
    private async Task ApplyGuardrailsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(Messages.Log.ApplyingGuardrails, guardrailSettings.Name, string.Join(", ", guardrailSettings.Deployments));

        var report = await guardrails.ApplyAsync(cancellationToken);
        if (!report.Passed)
        {
            throw new GuardrailsNotAppliedException(report);
        }

        logger.LogInformation(Messages.Log.GuardrailsApplied, report.Guardrail);
    }
}
