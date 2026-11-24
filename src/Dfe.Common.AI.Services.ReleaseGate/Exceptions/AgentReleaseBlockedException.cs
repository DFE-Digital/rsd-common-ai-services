using Dfe.Common.AI.Services.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Reporting;
using GovUK.Dfe.AI.Agents.Evaluation.ValueObjects;

namespace Dfe.Common.AI.Services.ReleaseGate.Exceptions;

/// <summary>
/// Thrown when the agents can't be released because the quality gate blocked one or more of them.
/// </summary>
/// <param name="report">The quality gate's decision, with each agent's reasons.</param>
public sealed class AgentReleaseBlockedException(QualityGateReport report)
    : InvalidOperationException(Messages.Errors.ReleaseBlocked(report))
{
    public QualityGateReport Report { get; } = report;

    /// <summary>The agents the quality gate didn't permit to be released.</summary>
    public IReadOnlyList<AgentReleaseDecision> Blocked { get; } = [.. report.Agents.Where(static agent => !agent.ReleasePermitted)];
}
