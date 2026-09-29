namespace Dfe.Common.AI.Services.Application.ValueObjects;

/// <summary>
/// The result of evaluating an agent against its test cases and judge scores. If the agent fails, the problems are listed.
/// </summary>
/// <param name="AgentName">The name of the agent tested.</param>
/// <param name="Problems">Why the agent can't be released. Empty when it can.</param>
public sealed record AgentGateResult(string AgentName, IReadOnlyList<string> Problems)
{
    public bool Passed => Problems.Count == 0;
}
