using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

public sealed class AnswerChecksTests
{
    private const string GiasLink = "https://get-information-schools.service.gov.uk/Establishments/Establishment/Details/999101";
    private const string Source = $"[Evidence 1] [Oakfield Primary School, URN 999101]({GiasLink}) (establishment_index)";
    private const string Cited = "398 pupils are on roll [Evidence 1].";

    public static TheoryData<string, string?> Answers => new()
    {
        { $"{Cited}\n\nSources:\n{Source}", null },
        { $"{Cited}\n\n**Sources**\n{Source}", null },
        { $"{Cited}\n\n## Sources\n{Source}", null },
        { $"{Cited}\n\nSources:\n[Evidence 1] [Oakfield](https://www.oakfield.sch.uk/about) (establishment_index)", null },
        { $"{Cited}\n\nSources:\n[Evidence 1] Oakfield Primary School, URN 999101 (establishment_index)", null },
        { "No evidence was supplied, so there is nothing to cite.", null },
        { " ", Messages.AnswerChecks.EmptyAnswer },
        { Cited, Messages.AnswerChecks.NoSources },
        {
            $"{Cited} It is in Westshire [Evidence 2].\n\nSources:\n{Source}",
            Messages.AnswerChecks.SourcesMissing([2])
        },
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public void Checks_answers_are_non_empty_and_list_the_sources_they_cite(string answer, string? expected) =>
        Assert.Equal(expected, AnswerChecks.Check(new AgentResult(AgentNames.Establishment, answer, TotalTokens: 0)));
}
