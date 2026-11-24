using Dfe.Common.AI.Services.ReleaseGate.Models;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Scenarios;
using GovUK.Dfe.AI.Agents.Options;
using GovUK.Dfe.AI.Agents.Quality;
using Microsoft.Extensions.Configuration;

namespace Dfe.Common.AI.Services.ReleaseGate.Tests.TestCases;

/// <summary>
/// The release gate's real test case files, as the agents library loads them from the output directory. The quality
/// gate blocks a release whose cases don't cover what it needs; checked here too, so a gap fails the build, not a
/// release. Which agents have cases, and what the cases may require, is checked against the agents themselves in
/// <c>CommonAgentsTests</c>.
/// </summary>
public sealed class TestCaseFileTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestCases");

    public static TheoryData<string> Agents => [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order()];

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_case_loads_and_follows_the_release_conventions(string agent)
    {
        var cases = await LoadAsync(agent);

        Assert.NotEmpty(cases);
        Assert.Empty(cases.SelectMany(ReleaseSections.Problems));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_case_names_its_responsible_ai_principle_risk_and_pass_condition(string agent)
    {
        var cases = await LoadAsync(agent);

        Assert.Empty(cases.SelectMany(testCase => ScenarioMetadata.Problems(testCase, requireMetadata: true)));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Tests_every_principle_its_cases_can_evidence(string agent)
    {
        var principles = (await LoadAsync(agent)).Select(testCase => ScenarioMetadata.For(testCase).Principle).ToHashSet();

        // Accountability is evidenced by the gate itself: traceability, test case conventions and attestations.
        Assert.Equal(Enum.GetValues<ResponsibleAiPrinciple>().Where(p => p != ResponsibleAiPrinciple.Accountability).Order(),
            principles.Order());
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task The_guardrail_section_tests_each_input_check_the_job_turns_on(string agent)
    {
        var cases = await LoadAsync(agent);
        var attacks = cases.Where(c => ReleaseSections.IsGuardrailCase(c.Name)).ToList();

        // One per check in appsettings.json: ScanPrompts, ScanEvidence, and CheckBlocklists on the question and the evidence.
        Assert.Equal(["injected-evidence", "jailbreak-prompt", "personal-identifier", "personal-identifier-in-evidence"],
            attacks.Select(c => c.Extra["name"].GetString()).Order(StringComparer.Ordinal));

        // The quality gate needs an attack on the question alone and one planted in the evidence.
        Assert.Contains(attacks, c => c.Evidence is null);
        Assert.Contains(attacks, c => c.Evidence is not null);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Plants_personal_data_the_answer_must_not_repeat(string agent)
    {
        var planted = (await LoadAsync(agent))
            .Where(c => !ReleaseSections.IsGuardrailCase(c.Name))
            .Select(c => (Case: c, Values: ScenarioMetadata.For(c).SensitiveData))
            .Where(c => c.Values.Count > 0)
            .ToList();

        Assert.NotEmpty(planted);

        // A value that isn't in the evidence can't be leaked, so the case would prove nothing.
        Assert.All(planted, c => Assert.All(c.Values, value => Assert.Contains(value, c.Case.Evidence)));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Expects_the_agent_to_say_when_the_evidence_doesnt_answer(string agent)
    {
        var uncertain = (await LoadAsync(agent)).Where(c => ScenarioMetadata.For(c).ExpectsUncertainty).ToList();

        Assert.NotEmpty(uncertain);

        // Each requires one of the prompt's fixed refusals, which the gate recognises and doesn't score.
        Assert.All(uncertain, c => Assert.Contains(c.MustMention,
            fact => FixedResponses.Refusals.Any(refusal => fact.Contains(refusal, StringComparison.OrdinalIgnoreCase))));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Compares_at_least_two_user_groups_each_with_enough_cases(string agent)
    {
        var minimum = new ConfigurationBuilder().AddJsonFile(AppSettings()).Build()
            .GetValue<int>("AiAgents:Evaluation:QualityGate:MinimumCasesPerGroup");
        var groups = (await LoadAsync(agent)).Where(c => c.Group is not null).GroupBy(c => c.Group!).ToList();

        Assert.True(minimum > 0, "The quality gate policy must set MinimumCasesPerGroup.");
        Assert.True(groups.Count >= 2, "The fairness checks compare groups, so at least two are needed.");
        Assert.All(groups, group => Assert.True(group.Count() >= minimum,
            $"Group \"{group.Key}\" has {group.Count()} cases; the quality gate needs at least {minimum} to compare it."));
    }

    private static Task<IReadOnlyList<AgentTestCase>> LoadAsync(string agent) =>
        AgentTestCase.LoadAsync(Path.Combine(Root, agent));

    /// <summary>The App's appsettings.json, which holds the quality gate's policy.</summary>
    private static string AppSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dfe.Common.Ai.Services.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "src", "Dfe.Common.Ai.Services.App", "appsettings.json");
    }
}
