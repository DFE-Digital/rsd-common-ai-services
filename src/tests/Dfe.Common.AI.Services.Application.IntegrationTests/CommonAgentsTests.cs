using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Options;
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
    public void Agent_requires_citations_and_rejects_empty_answers(string agentName)
    {
        var agent = Agent(agentName);

        Assert.True(agent.RequireCitations);
        Assert.NotNull(agent.Validate);
        Assert.Equal(Messages.AnswerChecks.EmptyAnswer, agent.Validate(new AgentResult(agentName, " ", TotalTokens: 0)));
        Assert.Null(agent.Validate(new AgentResult(agentName, "398 pupils are on roll [Evidence 1].", TotalTokens: 0)));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Agent_has_a_system_prompt_file(string agentName)
    {
        var prompt = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{Agent(agentName).SystemPromptType}.md");

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

    private static AgentDefinition Agent(string name) => CommonAgents.All.Single(a => a.Name == name);
}
