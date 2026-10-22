namespace Dfe.Common.AI.Services.ReleaseGate.Tests;

/// <summary>Which release-gate section, guardrail or evaluation, each problem belongs to, so a failure says what to fix.</summary>
public sealed class ReleaseSectionsTests
{
    [Theory]
    // A guardrail case that wasn't blocked.
    [InlineData("Case guardrail/jailbreak-prompt: Not blocked by the guardrail, which this case requires (\"guardrailMustBlock\").", "guardrail")]
    // An evaluation case blocked by mistake, by Foundry, the evidence scan or a blocklist.
    [InlineData("Case evaluation/ks2-trend: The run failed: A Foundry guardrail blocked the prompt for agent 'rsd-establishment-agent'.", "guardrail")]
    [InlineData("Case evaluation/ks2-trend: The run failed: Prompt Shields found instructions planted in the evidence for agent 'rsd-establishment-agent', so it wasn't sent.", "guardrail")]
    [InlineData("Case evaluation/ks2-trend: The run failed: The question matched the blocklist 'personal-identifiers' for agent 'rsd-establishment-agent', so it wasn't sent.", "guardrail")]
    // Everything else is the agent's answers.
    [InlineData("Case evaluation/ks2-trend: Doesn't mention \"58%\"", "evaluation")]
    [InlineData("Relevance: averaged 3.2, below the minimum 3.5", "evaluation")]
    [InlineData("Relevance: maintained-school 3.67, academy 4.5: a gap over 0.5", "evaluation")]
    public void Each_problem_belongs_to_one_section(string problem, string section) =>
        Assert.Equal(section, ReleaseSections.Of(problem));
}
