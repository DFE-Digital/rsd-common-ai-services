using GovUK.Dfe.AI.Agents.Quality;

namespace Dfe.Common.AI.Services.ReleaseGate.Tests.TestCases;

/// <summary>The release gate's real test case files, as the agents library loads them from the output directory.</summary>
public sealed class TestCaseFileTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestCases");

    public static TheoryData<string> Agents => [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order()];

    [Fact]
    public void Ships_test_cases_for_both_agents() =>
        Assert.Equal(["rsd-establishment-agent", "rsd-trust-agent"], Directory.GetDirectories(Root).Select(Path.GetFileName).Order());

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_case_loads_and_is_in_the_evaluation_or_guardrail_folder(string agent)
    {
        var cases = await AgentTestCase.LoadAsync(Path.Combine(Root, agent));

        Assert.NotEmpty(cases);
        Assert.All(cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Prompt), c.Name));
        Assert.All(cases, c => Assert.True(c.Name.StartsWith(ReleaseSections.EvaluationCasePrefix, StringComparison.Ordinal)
            || c.Name.StartsWith(ReleaseSections.GuardrailCasePrefix, StringComparison.Ordinal), c.Name));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task The_guardrail_section_tests_each_check_and_every_case_in_it_must_be_blocked(string agent)
    {
        var cases = await AgentTestCase.LoadAsync(Path.Combine(Root, agent));
        var guardrail = cases.Where(c => c.Name.StartsWith(ReleaseSections.GuardrailCasePrefix, StringComparison.Ordinal)).ToList();

        // One per check the app runs: ScanEvidence, ScanPrompts and CheckBlocklists.
        Assert.Equal(["guardrail/injected-evidence", "guardrail/jailbreak-prompt", "guardrail/personal-identifier"],
            guardrail.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.All(guardrail, c => Assert.True(c.GuardrailMustBlock, c.Name));
        Assert.All(guardrail, c => Assert.Null(c.Group));   // never scored, so not in a fairness group
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task No_evaluation_case_lets_a_guardrail_block_it(string agent)
    {
        var cases = await AgentTestCase.LoadAsync(Path.Combine(Root, agent));

        // A block of an evaluation case is a false positive, so it fails the case and counts in the guardrail section.
        Assert.All(cases.Where(c => c.Name.StartsWith(ReleaseSections.EvaluationCasePrefix, StringComparison.Ordinal)),
            c => Assert.False(c.GuardrailMayBlock || c.GuardrailMustBlock, c.Name));
    }
}
