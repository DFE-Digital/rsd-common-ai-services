using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Exceptions;

/// <summary>
/// Thrown when the Foundry guardrail couldn't be put in place on every model deployment, for example because a
/// deployment name is wrong. Nothing is tested or provisioned.
/// </summary>
/// <param name="report">The guardrail and what's wrong with it.</param>
public sealed class GuardrailsNotAppliedException(GuardrailReport report)
    : InvalidOperationException(Messages.Errors.GuardrailsNotApplied(report.Guardrail, report.Problems))
{
    public GuardrailReport Report { get; } = report;
}
