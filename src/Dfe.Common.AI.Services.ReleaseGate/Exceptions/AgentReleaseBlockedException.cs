using Dfe.Common.AI.Services.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;

namespace Dfe.Common.AI.Services.ReleaseGate.Exceptions;

/// <summary>
/// Thrown when the agents can't be released because one or more of them failed the release gate checks.
/// </summary>
/// <param name="failed">The agents that failed, each with its problems.</param>
public sealed class AgentReleaseBlockedException(IReadOnlyList<AgentGateResult> failed)
    : InvalidOperationException(Messages.Errors.ReleaseBlocked(failed))
{
    public IReadOnlyList<AgentGateResult> Failed { get; } = failed;
}
