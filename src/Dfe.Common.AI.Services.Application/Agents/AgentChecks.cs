using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// An app's own checks on a common agent's answers, run after the agent's own (<see cref="AnswerChecks"/>). A failed
/// check sends the answer back to the agent once with the reason; if the second answer fails too, the run fails.
/// </summary>
public static class AgentChecks
{
    /// <summary>
    /// The agent with <paramref name="check"/> added after its own checks, e.g. one an app validates its answers with.
    /// </summary>
    /// <param name="check">Returns why an answer is wrong, or null if it's fine.</param>
    public static AgentDefinition WithCheck(this AgentDefinition agent, Func<AgentResult, string?> check)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(check);

        var own = agent.Validate;
        return agent with { Validate = result => own?.Invoke(result) ?? check(result) };
    }

    /// <summary>
    /// The agent with a check that its answer addresses each outcome the app expects, e.g. "Outstanding" (case-insensitive).
    /// It never makes the agent agree: an answer that leaves one out is sent back to include it only if the evidence
    /// supports it, and otherwise to say plainly that the evidence doesn't show it. Either way the outcome is then
    /// mentioned, so the app reads the answer itself to see whether its expectation held.
    /// </summary>
    public static AgentDefinition ExpectingMention(this AgentDefinition agent, params string[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.Length == 0 || expected.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(Messages.Errors.NoExpectedOutcome, nameof(expected));
        }

        return agent.WithCheck(result =>
        {
            var answer = result.Output ?? string.Empty;
            var missing = expected.Where(outcome => !answer.Contains(outcome, StringComparison.OrdinalIgnoreCase)).ToList();
            return missing.Count == 0 ? null : Messages.AnswerChecks.ExpectedOutcomeNotAddressed(missing);
        });
    }
}
