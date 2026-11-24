namespace Dfe.Common.AI.Services.ReleaseGate.Models;

/// <summary>
/// The fixed refusals in the agents' system prompts (Prompts/*.md), as test cases require them.
/// </summary>
public static class FixedResponses
{
    public const string NotInEvidence = "does not include this information";

    public const string OutOfScope = "outside what I can answer";

    public static IReadOnlyList<string> Refusals { get; } = [NotInEvidence, OutOfScope];
}
