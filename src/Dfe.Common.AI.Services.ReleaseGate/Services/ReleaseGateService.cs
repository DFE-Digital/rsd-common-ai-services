using Dfe.Common.AI.Services.ReleaseGate.Constants;
using Dfe.Common.AI.Services.ReleaseGate.Exceptions;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.Options;
using GovUK.Dfe.AI.Agents.Guardrails.Services.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Logging;
using Dfe.Common.AI.Services.ReleaseGate.Services.Interfaces;
using Dfe.Common.AI.Services.ReleaseGate.Metrics;

namespace Dfe.Common.AI.Services.ReleaseGate.Services;

/// <inheritdoc />
public sealed class ReleaseGateService(IAgentReleaseGate releaseGate, IFoundryGuardrailsService guardrails,
    GuardrailSettings guardrailSettings,
    ILogger<ReleaseGateService> logger) : IReleaseGateService
{
    public async Task CheckAsync(IReadOnlyList<AgentDefinition> definitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        // The guardrail goes on first, so the release checks test the agents behind it, as they'll run in production.
        await CheckGuardrailAsync(definitions, cancellationToken);

        logger.ReleaseChecksStarting(definitions.Count, string.Join(", ", definitions.Select(a => a.Name)));
        var gate = Results(definitions, await releaseGate.EvaluateAsync([.. definitions], cancellationToken));
        gate.ForEach(LogSections);

        var failed = gate.Where(result => !result.Passed).ToList();
        if (failed.Count != 0)
        {
            var failedNames = string.Join(", ", failed.Select(r => r.AgentName));
            logger.ReleaseChecksFailed(failed.Count, failedNames);
            throw new AgentReleaseBlockedException(failed);
        }
    }

    /// <summary>
    /// Confirms the Foundry guardrail, created in the Foundry portal, exists with its blocklists and is at least as strict
    /// as the settings. The job never creates or changes it, so it needs only Reader on the Foundry resource. The library
    /// puts the guardrail on every agent version it creates.
    /// </summary>
    private async Task CheckGuardrailAsync(IReadOnlyList<AgentDefinition> definitions, CancellationToken cancellationToken)
    {
        logger.CheckingGuardrail(guardrailSettings.Name, guardrailSettings.Targets);

        var report = await guardrails.CheckAsync(cancellationToken);
        ReleaseGateMetrics.RecordGuardrailCheck(definitions, report.Passed);
        if (!report.Passed)
        {
            throw new GuardrailNotInPlaceException(report);
        }

        logger.GuardrailInPlace(report.Guardrail);
    }

    /// <summary>
    /// Every agent's result, with a failure for any the gate returned no result for, so an agent is only released when
    /// it was tested and passed.
    /// </summary>
    private static List<AgentGateResult> Results(IReadOnlyList<AgentDefinition> definitions, IReadOnlyList<AgentGateResult> gate) =>
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
}
