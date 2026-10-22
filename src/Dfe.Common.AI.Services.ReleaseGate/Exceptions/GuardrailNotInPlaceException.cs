using Dfe.Common.AI.Services.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;

namespace Dfe.Common.AI.Services.ReleaseGate.Exceptions;

/// <summary>
/// Thrown when the Foundry guardrail or one of its blocklists is missing, or weaker than the settings. It's created in
/// the Foundry portal, not by this job. Nothing is tested or provisioned.
/// </summary>
/// <param name="report">The guardrail and what's wrong with it.</param>
public sealed class GuardrailNotInPlaceException(GuardrailReport report)
    : InvalidOperationException(Messages.Errors.GuardrailNotInPlace(report.Guardrail, report.Problems))
{
    public GuardrailReport Report { get; } = report;
}
