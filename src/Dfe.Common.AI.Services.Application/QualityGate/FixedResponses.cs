using GovUK.Dfe.CoreLibs.AiAgents.Quality;

namespace Dfe.Common.AI.Services.Application.QualityGate;

/// <summary>
/// The fixed refusals in the agents' system prompts (Prompts/*.md), as test cases require them.
/// </summary>
public static class FixedResponses
{
    public const string NotInEvidence = "does not include this information";

    public const string OutOfScope = "outside what I can answer";

    public static IReadOnlyList<string> Refusals { get; } = [NotInEvidence, OutOfScope];

    /// <summary>
    /// Whether a test case expects the agent to refuse. Its required refusal is the check; the judge's relevance
    /// score marks any refusal as irrelevant, so these cases are left out of the average scores.
    /// </summary>
    public static bool ExpectsRefusal(AgentTestCase testCase) =>
        testCase.MustMention.Any(text => Refusals.Any(refusal => text.Contains(refusal, StringComparison.OrdinalIgnoreCase)));
}
