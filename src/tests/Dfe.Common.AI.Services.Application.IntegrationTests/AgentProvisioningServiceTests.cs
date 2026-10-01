using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.CoreLibs.AiAgents.Quality;
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
        await _host.Agents.Received(CommonAgents.All.Length).ProvisionAsync(
            Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => d.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Runs_test_cases_in_parallel_up_to_the_limit_and_reports_them_in_order()
    {
        _host.AddCaseForEveryAgent(caseName: "a-first");
        _host.AddCaseForEveryAgent(caseName: "b-second");
        var running = 0;
        var mostAtOnce = 0;
        _host.Agents.RunAsync(default!, default!, default, default).ReturnsForAnyArgs(async call =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref mostAtOnce, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref running);
            return new AgentResult(call.Arg<AgentDefinition>().Name, "398 pupils are on roll [Evidence 1].", TotalTokens: 0);
        });

        await _host.Build(maxParallelTestRuns: 2).ProvisionAsync();

        Assert.Equal(2, mostAtOnce);
        foreach (var agent in AgentNames.All)
        {
            var report = await File.ReadAllTextAsync(Path.Combine(_host.ReportsDirectory, $"{agent}.json"));
            Assert.True(report.IndexOf("a-first", StringComparison.Ordinal) < report.IndexOf("b-second", StringComparison.Ordinal));
    }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
            // Another run raised it at the same time; read it again.
        }
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
    public async Task Leaves_cases_expecting_a_refusal_out_of_the_judge_averages()
    {
        const string Weather = "What will the weather be like tomorrow?";
        _host.AddCaseForEveryAgent(caseName: "answered");
        _host.AddCaseForEveryAgent(caseName: "out-of-scope", prompt: Weather, mustMention: [FixedResponses.OutOfScope]);
        _host.AnswerWith("This is outside what I can answer. 398 pupils are on roll [Evidence 1].");
        _host.Judge.EvaluateAsync(default!, default).ReturnsForAnyArgs(call =>
            JudgeMetrics.All.ToDictionary(metric => metric, _ => call.Arg<AgentRunSample>().Prompt == Weather ? 1.0 : 5.0));

        var provisioned = await _host.Build(JudgeModel).ProvisionAsync();

        Assert.Equal(CommonAgents.All.Length, provisioned.Count);
        Assert.Contains(_host.Logs, l => l.Message.EndsWith("(1 cases expecting a refusal not scored)"));
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
