using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.AI.Agents.Quality;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

public sealed class GuardrailBlocksTests
{
    private static readonly Dictionary<string, double> NoScores = [];

    public static TheoryData<string?, string, bool> Results => new()
    {
        // Prompt blocked: the runner records Foundry's error, which carries the content_filter code.
        { null, "The run failed: Service request failed. Status: 400 (Bad Request) {\"error\":{\"code\":\"content_filter\"}}", true },
        // Answer blocked: the runner records the library's own message.
        { null, "The run failed: A Foundry guardrail blocked the answer for agent 'rsd-trust-agent'.", true },
        // Any other failed run, or a wrong answer, isn't a block.
        { null, "The run failed: The operation timed out.", false },
        { "The trust is closing.", "Mentions \"closing\"", false },
    };

    [Theory]
    [MemberData(nameof(Results))]
    public void Recognises_only_runs_a_guardrail_blocked(string? output, string failure, bool blocked) =>
        Assert.Equal(blocked, GuardrailBlocks.IsBlocked(new AgentTestResult("case", output, [failure], NoScores)));

    [Fact]
    public void A_passed_result_is_not_a_block() =>
        Assert.False(GuardrailBlocks.IsBlocked(new AgentTestResult("case", "398 pupils [Evidence 1].", [], NoScores)));
}
