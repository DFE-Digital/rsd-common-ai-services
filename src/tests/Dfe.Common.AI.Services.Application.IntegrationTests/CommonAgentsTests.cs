using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.CoreLibs.AiAgents.Quality;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

public sealed class CommonAgentsTests
{
    private static readonly string TestCasesRoot =
        Path.Combine(AppContext.BaseDirectory, new AgentQualityOptions().TestCasesDirectory);

    public static TheoryData<string> Names => [.. AgentNames.All];

    [Fact]
    public void Agent_names_are_unchanged()
    {
        Assert.Equal(AgentNames.All, CommonAgents.All.Select(a => a.Name));
    }

    [Fact]
    public void Every_test_case_folder_belongs_to_an_agent()
    {
        var folders = Directory.GetDirectories(TestCasesRoot).Select(Path.GetFileName);

        Assert.Equal(AgentNames.All.Order(), folders.Order());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Agent_requires_citations_with_sources_and_rejects_empty_answers(string agentName)
    {
        var agent = Agent(agentName);

        Assert.True(agent.RequireCitations);
        Assert.NotNull(agent.Validate);
        Assert.Equal(Messages.AnswerChecks.EmptyAnswer, agent.Validate(new AgentResult(agentName, " ", TotalTokens: 0)));
        Assert.Equal(Messages.AnswerChecks.NoSources, agent.Validate(new AgentResult(agentName, "398 pupils [Evidence 1].", TotalTokens: 0)));
        Assert.Null(agent.Validate(new AgentResult(agentName,
            "398 pupils [Evidence 1].\n\nSources:\n[Evidence 1] Oakfield Primary School, URN 999101 (establishment_index)",
            TotalTokens: 0)));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Agent_has_a_system_prompt_file(string agentName)
    {
        var prompt = PromptPath(agentName);

        Assert.True(File.Exists(prompt), $"Missing {prompt}");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Agent_ships_test_cases_with_citable_evidence(string agentName)
    {
        var cases = await AgentTestCase.LoadAsync(Path.Combine(TestCasesRoot, agentName));

        Assert.NotEmpty(cases);
        var withoutCitableEvidence = cases
            .Where(c => c.Evidence is null || !c.Evidence.Contains("Evidence 1 ---"))
            .Select(c => c.Name);
        Assert.Empty(withoutCitableEvidence);
    }

    /// <summary>
    /// A case can only require text a good answer could contain: a fact from its evidence, a detail from its question,
    /// or one of the fixed responses in the agent's system prompt. So rewording a fixed response fails here, naming
    /// the cases that rely on it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public async Task Agent_test_cases_only_require_text_a_good_answer_can_contain(string agentName)
    {
        var systemPrompt = Normalise(await File.ReadAllTextAsync(PromptPath(agentName)));
        var cases = await AgentTestCase.LoadAsync(Path.Combine(TestCasesRoot, agentName));

        var unreachable = cases.SelectMany(c => c.MustMention
            .Where(fact => !new[] { c.Evidence ?? "", c.Prompt, systemPrompt }.Any(source => Normalise(source).Contains(Normalise(fact))))
            .Select(fact => $"{c.Name}: \"{fact}\""));
        var inQuestion = cases.SelectMany(c => c.MustNotMention
            .Where(fact => Normalise(c.Prompt).Contains(Normalise(fact)))
            .Select(fact => $"{c.Name}: \"{fact}\""));

        Assert.Empty(unreachable);
        Assert.Empty(inQuestion);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Agent_prompt_gives_the_fixed_refusals_the_gate_recognises(string agentName)
    {
        var systemPrompt = Normalise(await File.ReadAllTextAsync(PromptPath(agentName)));

        var missing = FixedResponses.Refusals.Where(refusal => !systemPrompt.Contains(Normalise(refusal)));

        Assert.Empty(missing);
    }

    private static AgentDefinition Agent(string name) => CommonAgents.All.Single(a => a.Name == name);

    private static string PromptPath(string agentName) =>
        Path.Combine(AppContext.BaseDirectory, "Prompts", $"{Agent(agentName).SystemPromptKey}.md");

    /// <summary>Lower case with runs of whitespace as one space, so line breaks in the prompt files don't matter.</summary>
    private static string Normalise(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
