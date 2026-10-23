using GovUK.Dfe.AI.Agents.Release;

namespace Dfe.Common.AI.Services.ReleaseGate.Tests.TestCases;

/// <summary>
/// The release gate's real test case files, as the agents library loads them from the output directory. Which agents
/// have cases, and what the cases may require, is checked against the agents themselves in <c>CommonAgentsTests</c>.
/// </summary>
public sealed class TestCaseFileTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestCases");

    public static TheoryData<string> Agents => [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order()];

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_case_loads_and_follows_the_release_conventions(string agent)
    {
        var cases = await AgentTestCase.LoadAsync(Path.Combine(Root, agent));

        // The release gate fails a case that doesn't; checked here too, so a wrong file fails the build, not a release.
        Assert.NotEmpty(cases);
        Assert.Empty(cases.SelectMany(ReleaseSections.Problems));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task The_guardrail_section_tests_each_input_check_the_job_turns_on(string agent)
    {
        var cases = await AgentTestCase.LoadAsync(Path.Combine(Root, agent));

        // One per check in appsettings.json: ScanEvidence, ScanPrompts and CheckBlocklists.
        Assert.Equal(["guardrail/injected-evidence", "guardrail/jailbreak-prompt", "guardrail/personal-identifier"],
            cases.Select(c => c.Name).Where(ReleaseSections.IsGuardrailCase).Order(StringComparer.Ordinal));
    }
}
