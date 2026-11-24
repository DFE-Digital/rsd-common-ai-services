using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Tests.Agents;

/// <summary>How an app adds its own checks, such as an expected outcome, to a common agent.</summary>
public sealed class AgentChecksTests
{
    private const string Cited = "The evidence shows Good, not Outstanding. [Evidence 1]\n\nSources:\n- [Evidence 1]";

    /// <summary>Runs the agent's checks on an answer, as the library does after each run.</summary>
    private static string? Check(AgentDefinition agent, string? output)
    {
        var validate = agent.Validate;
        Assert.NotNull(validate);
        return validate(new AgentResult { AgentName = agent.Name, Output = output });
    }

    [Fact]
    public void An_apps_check_runs_after_the_agents_own_which_it_cant_drop()
    {
        var calls = 0;
        var agent = CommonAgents.Establishment.WithCheck(_ =>
        {
            calls++;
            return "The app's own reason.";
        });

        Assert.Equal(Messages.AnswerChecks.EmptyAnswer, Check(agent, ""));   // the common check still runs, first
        Assert.Equal(0, calls);
        Assert.Equal("The app's own reason.", Check(agent, Cited));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Adding_a_check_leaves_the_shared_definition_unchanged()
    {
        _ = CommonAgents.Establishment.WithCheck(_ => "Never.");

        Assert.Null(Check(CommonAgents.Establishment, Cited));
    }

    [Theory]
    [InlineData("Overall effectiveness: Good. [Evidence 1]\n\nSources:\n- [Evidence 1]")]   // matches the expectation
    [InlineData(Cited)]                                                                     // contradicts it, plainly
    public void An_expected_outcome_passes_once_the_answer_addresses_it_either_way(string output)
    {
        var agent = CommonAgents.Establishment.ExpectingMention("good");   // case-insensitive

        Assert.Null(Check(agent, output));
    }

    [Fact]
    public void An_answer_that_ignores_an_expected_outcome_goes_back_to_check_the_evidence_not_to_agree()
    {
        var agent = CommonAgents.Establishment.ExpectingMention("Outstanding", "2022");

        var reason = Check(agent, "Overall effectiveness: Good, 8 February 2022. [Evidence 1]\n\nSources:\n- [Evidence 1]");

        Assert.Equal(Messages.AnswerChecks.ExpectedOutcomeNotAddressed(["Outstanding"]), reason);
        Assert.Contains("if not, say plainly that the evidence doesn't show it", reason, StringComparison.Ordinal);
        Assert.Contains("Never state it just because it was expected", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_expected_outcome_must_be_given()
    {
        Assert.Throws<ArgumentException>(() => CommonAgents.Trust.ExpectingMention());
        Assert.Throws<ArgumentException>(() => CommonAgents.Trust.ExpectingMention(" "));
    }
}
