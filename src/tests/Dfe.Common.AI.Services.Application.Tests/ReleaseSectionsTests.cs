using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.Tests;

/// <summary>Which release-gate section, guardrail or evaluation, each problem belongs to.</summary>
public sealed class ReleaseSectionsTests : IDisposable
{
    private readonly ProvisioningTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory]
    // A guardrail case that wasn't blocked.
    [InlineData("Case guardrail/jailbreak-prompt: Not blocked by the guardrail, which this case requires (\"guardrailMustBlock\").", "guardrail")]
    // A normal case blocked by mistake, by Foundry or by the evidence scan.
    [InlineData("Case evaluation/ks2-trend: The run failed: A Foundry guardrail blocked the prompt for agent 'rsd-establishment-agent'.", "guardrail")]
    [InlineData("Case evaluation/ks2-trend: The run failed: Prompt Shields found instructions planted in the evidence for agent 'rsd-establishment-agent', so it wasn't sent.", "guardrail")]
    [InlineData("Case evaluation/ks2-trend: The run failed: The question matched the blocklist 'personal-identifiers' for agent 'rsd-establishment-agent', so it wasn't sent.", "guardrail")]
    // Everything else is the agent's answers.
    [InlineData("Case evaluation/ks2-trend: Doesn't mention \"58%\"", "evaluation")]
    [InlineData("Relevance: averaged 3.2, below the minimum 3.5", "evaluation")]
    [InlineData("Relevance: maintained-school 3.67, academy 4.5: a gap over 0.5", "evaluation")]
    public void Each_problem_belongs_to_one_section(string problem, string section) =>
        Assert.Equal(section, ReleaseSections.Of(problem));

    [Fact]
    public void A_blocked_release_lists_each_problem_with_its_section_guardrail_first()
    {
        var failed = new AgentGateResult(AgentNames.Trust,
            ["Case evaluation/trust-profile: Doesn't mention \"31.4\"", "Case guardrail/personal-identifier: Not blocked by the guardrail."]);

        var lines = Messages.Errors.ReleaseBlocked([failed]).Split(Environment.NewLine);

        Assert.Equal(
            [
                "No agents were provisioned because these failed their release checks:",
                $"- {AgentNames.Trust} [guardrail]: Case guardrail/personal-identifier: Not blocked by the guardrail.",
                $"- {AgentNames.Trust} [evaluation]: Case evaluation/trust-profile: Doesn't mention \"31.4\"",
            ],
            lines);
    }

    [Fact]
    public async Task Logs_each_agents_guardrail_and_evaluation_result()
    {
        _host.AddCaseForEveryAgent(mustMention: ["398"]);
        _host.AddCaseForEveryAgent(caseName: "guardrail/injected-evidence", guardrailMustBlock: true);
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");   // answers, so the guardrail case isn't blocked

        await Assert.ThrowsAsync<Exceptions.AgentReleaseBlockedException>(() => _host.Build().ProvisionAsync());

        foreach (var agent in AgentNames.All)
        {
            Assert.Contains(_host.Logs, l => l.Level == LogLevel.Warning
                && l.Message == $"{agent}: guardrail failed (1 problems), evaluation passed");
        }
    }
}
