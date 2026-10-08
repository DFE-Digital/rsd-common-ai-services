using System.Text.Json;
using GovUK.Dfe.AI.Agents.Quality;

namespace Dfe.Common.AI.Services.Application.QualityGate;

/// <summary>
/// The Foundry guardrail blocks prompts and answers it judges harmful, including instructions planted in evidence.
/// A test case that plants such an instruction can set <c>"guardrailMayBlock": true</c>, so a block counts as a pass.
/// A block on any other case still fails it: that's a false positive worth knowing about.
/// </summary>
public static class GuardrailBlocks
{
    public const string AllowedProperty = "guardrailMayBlock";

    // The library's test runner records a failed run as "The run failed: " and its innermost error. A blocked answer
    // gives the library's guardrail message; a blocked prompt gives Foundry's content_filter error, which the library
    // itself uses to recognise the block. Remove this matching if the library reports blocks on test results.
    private const string RunFailed = "The run failed: ";
    private const string AnswerBlocked = "A Foundry guardrail blocked";
    private const string PromptBlocked = "content_filter";

    public static bool IsBlocked(AgentTestResult result) =>
        result is { Passed: false, Output: null, Failures: [var failure] }
        && failure.StartsWith(RunFailed, StringComparison.Ordinal)
        && (failure.Contains(AnswerBlocked, StringComparison.Ordinal) || failure.Contains(PromptBlocked, StringComparison.Ordinal));

    /// <summary>Whether the test case file sets <c>"guardrailMayBlock": true</c>.</summary>
    public static async Task<bool> AllowedForAsync(string caseFile, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(caseFile);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return json.RootElement.TryGetProperty(AllowedProperty, out var allowed) && allowed.ValueKind == JsonValueKind.True;
    }
}
