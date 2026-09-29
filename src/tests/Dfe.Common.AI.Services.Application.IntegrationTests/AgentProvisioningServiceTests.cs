using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

public sealed class AgentProvisioningServiceTests : IDisposable
{
    private const string JudgeModel = "judge-model";

    private readonly ProvisioningTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Provisions_every_agent_after_testing_ephemeral_copies_when_all_pass()
    {
        _host.AddCaseForEveryAgent(mustMention: ["398"]);
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");

        var provisioned = await _host.Build().ProvisionAsync();

        Assert.Equal(CommonAgents.All.Select(a => a.Name), provisioned.Select(a => a.Name));
        await _host.Agents.DidNotReceive().RunAsync(
            Arg.Is<AgentDefinition>(d => d.IsManagedAgent), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logs_progress_for_each_agent_and_test_case()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");

        await _host.Build().ProvisionAsync();

        var progress = _host.Logs.Where(l => l.Level == LogLevel.Information).Select(l => l.Message).ToList();
        foreach (var agent in AgentNames.All)
        {
            Assert.Contains(progress, m => m.StartsWith($"Testing {agent} with 1 test cases"));
            Assert.Contains($"{agent} / case: passed", progress);
            Assert.Contains(progress, m => m.StartsWith($"{agent} passed its release checks"));
        }
    }

    [Fact]
    public async Task Progress_can_be_turned_off_in_settings_while_failures_still_show()
    {
        _host.AddCaseForEveryAgent(mustNotMention: ["Inadequate"]);
        _host.AnswerWith("The school is Inadequate [Evidence 1].");

        await Assert.ThrowsAsync<AgentReleaseBlockedException>(
            () => _host.Build(applicationLogLevel: "Warning").ProvisionAsync());

        Assert.DoesNotContain(_host.Logs, l => l.Level == LogLevel.Information);
        Assert.Contains(_host.Logs, l => l.Level == LogLevel.Error && l.Message.StartsWith("Agent release checks failed"));
    }

    [Fact]
    public async Task Writes_a_report_for_every_agent()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");

        await _host.Build().ProvisionAsync();

        var missing = CommonAgents.All.Where(a => !File.Exists(Path.Combine(_host.ReportsDirectory, $"{a.Name}.json")));
        Assert.Empty(missing);
    }

    [Fact]
    public async Task Blocks_release_when_an_answer_mentions_a_forbidden_fact()
    {
        _host.AddCaseForEveryAgent(mustNotMention: ["Inadequate"]);
        _host.AnswerWith("The school is Inadequate [Evidence 1].");

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build().ProvisionAsync());

        Assert.Equal(CommonAgents.All.Length, ex.Failed.Count);
        Assert.Contains("Mentions \"Inadequate\"", ex.Message);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Deletes_leftover_test_agents_when_release_is_blocked()
    {
        _host.AddCaseForEveryAgent(mustNotMention: ["Inadequate"]);
        _host.AnswerWith("The school is Inadequate [Evidence 1].");

        await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build().ProvisionAsync());

        await _host.Runtime.Received(1).DeleteOrphanedEphemeralAgentsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deletes_leftover_test_agents_when_the_gate_throws()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Judge.EvaluateAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("Judge failed."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.Build(JudgeModel).ProvisionAsync());

        await _host.Runtime.Received(1).DeleteOrphanedEphemeralAgentsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_clean_up_does_not_block_release()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Runtime.DeleteOrphanedEphemeralAgentsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Foundry unavailable."));

        var provisioned = await _host.Build().ProvisionAsync();

        Assert.Equal(CommonAgents.All.Length, provisioned.Count);
    }

    [Fact]
    public async Task Blocks_release_when_an_agent_has_no_test_cases()
    {
        var tested = CommonAgents.All[0].Name;
        _host.AddCase(tested);
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build().ProvisionAsync());

        Assert.Equal(CommonAgents.All.Skip(1).Select(a => a.Name), ex.Failed.Select(r => r.AgentName));
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Blocks_release_when_judge_scores_are_below_the_minimum()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.JudgeScores(2.0);

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build(JudgeModel).ProvisionAsync());

        var minimum = new AgentQualityOptions().MinimumScore;
        Assert.Contains(Messages.ReleaseGate.ScoreBelowMinimum(JudgeMetrics.All[0], 2.0, minimum), ex.Message);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Blocks_release_when_the_judge_gives_no_score()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Judge.EvaluateAsync(default!, default)
            .ReturnsForAnyArgs((IReadOnlyDictionary<string, double>)new Dictionary<string, double>());

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build(JudgeModel).ProvisionAsync());

        foreach (var metric in JudgeMetrics.All)
        {
            Assert.Contains(Messages.ReleaseGate.NotScored(metric), ex.Message);
        }
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Provisions_when_judge_scores_meet_the_minimum()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.JudgeScores(4.5);

        var provisioned = await _host.Build(JudgeModel).ProvisionAsync();

        Assert.Equal(CommonAgents.All.Length, provisioned.Count);
        await _host.Judge.ReceivedWithAnyArgs(CommonAgents.All.Length).EvaluateAsync(default!, default);
    }
}
