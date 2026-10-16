using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Tests;

public sealed class AnswerChecksTests
{
    private const string Cited = "398 pupils are on roll [Evidence 1].";

    public static TheoryData<string, string?> Answers => new()
    {
        { $"{Cited}\n\nSources:\n- [Evidence 1]", null },
        { $"{Cited}\n\n**Sources**\n- [Evidence 1]", null },
        { $"{Cited}\n\n## Sources\n[Evidence 1]", null },
        { $"{Cited} The website is https://www.oakfield.sch.uk [Evidence 1].\n\nSources:\n- [Evidence 1]", null },
        { "No evidence was supplied, so there is nothing to cite.", null },
        { " ", Messages.AnswerChecks.EmptyAnswer },
        { Cited, Messages.AnswerChecks.NoSources },
        { $"{Cited} It is in Westshire [Evidence 2].\n\nSources:\n- [Evidence 1]", Messages.AnswerChecks.SourcesMissing([2]) },
        {
            $"{Cited}\n\nSources:\n- [Evidence 1] [Oakfield](https://get-information-schools.service.gov.uk/999101)",
            Messages.AnswerChecks.LinksInSources
        },
        { $"{Cited}\n\nSources:\n- [Evidence 1] get-information-schools.service.gov.uk", null },
        { $"{Cited}\n\nSources:\n- [Evidence 1] www.oakfield.sch.uk", Messages.AnswerChecks.LinksInSources },
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public void Checks_answers_are_non_empty_and_list_their_sources_as_citations_only(string answer, string? expected) =>
        Assert.Equal(expected, AnswerChecks.Check(new AgentResult { AgentName = AgentNames.Establishment, Output = answer }));
}
