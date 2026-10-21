using System.Diagnostics;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
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
        var agentNames = string.Join(", ", definitions.Select(a => a.Name));
        logger.Starting(definitions.Length, agentNames);

        // The guardrail goes on first, so the release checks test the agents behind it, as they'll run in production.
        await CheckGuardrailAsync(definitions, cancellationToken);

        logger.ReleaseChecksStarting(definitions.Length, agentNames);
        var gate = Results(definitions, await releaseGate.EvaluateAsync(definitions, cancellationToken));
        gate.ForEach(LogSections);
        var failed = gate.Where(result => !result.Passed).ToList();
        if (failed.Count != 0)
        {
            var failedNames = string.Join(", ", failed.Select(r => r.AgentName));
            logger.ReleaseChecksFailed(failed.Count, failedNames);
            throw new AgentReleaseBlockedException(failed);
        }

        // The library provisions the agents it's given one after another, so each agent gets its own call and they
        // run side by side. The library logs each agent's name and version as it's provisioned.
        logger.Provisioning();
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

        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        logger.ProvisioningFinished(definitions.Length, seconds);
        var provisioned = provisioning.SelectMany(task => task.Result).ToList();
        ReleaseMetrics.RecordProvisioned(provisioned);
        foreach (var agent in provisioned)
        {
            logger.AgentVersion(agent.Name, agent.Version);
        }

        return provisioned;
    }

    /// <summary>
    /// Every agent's result, with a failure for any the gate returned no result for, so an agent is only provisioned
    /// when it was tested and passed.
    /// </summary>
    private static List<AgentGateResult> Results(AgentDefinition[] definitions, IReadOnlyList<AgentGateResult> gate) =>
        [.. definitions.Select(definition => gate.FirstOrDefault(r => r.AgentName == definition.Name)
            ?? new AgentGateResult(definition.Name, [Messages.Errors.NotTested]))];

    /// <summary>One line per agent: whether its guardrail and evaluation sections passed.</summary>
    private void LogSections(AgentGateResult result)
    {
        var guardrail = Messages.Errors.SectionResult(ReleaseSections.Problems(result, ReleaseSections.Guardrail).Count);
        var evaluation = Messages.Errors.SectionResult(ReleaseSections.Problems(result, ReleaseSections.Evaluation).Count);
        if (result.Passed)
        {
            logger.ReleaseSectionsPassed(result.AgentName, guardrail, evaluation);
        }
        else
        {
            logger.ReleaseSectionsFailed(result.AgentName, guardrail, evaluation);
        }
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

        ReleaseMetrics.RecordProvisioned(provisioned);
        var failed = definitions.Where((_, index) => !provisioning[index].IsCompletedSuccessfully).Select(d => d.Name).ToList();
        throw new AgentProvisioningIncompleteException(provisioned, failed, failure);
    }

    /// <summary>
    /// Confirms the Foundry guardrail, created in the Foundry portal, exists with its blocklists and is at least as strict
    /// as the settings. The job never creates or changes it, so it needs only Reader on the Foundry resource. The library
    /// puts the guardrail on every agent version it creates.
    /// </summary>
    private async Task CheckGuardrailAsync(AgentDefinition[] definitions, CancellationToken cancellationToken)
    {
        logger.CheckingGuardrail(guardrailSettings.Name, guardrailSettings.Targets);

        var report = await guardrails.CheckAsync(cancellationToken);
        ReleaseMetrics.RecordGuardrailCheck(definitions, report.Passed);
        if (!report.Passed)
        {
            throw new GuardrailNotInPlaceException(report);
        }

        logger.GuardrailInPlace(report.Guardrail);
    }
}
