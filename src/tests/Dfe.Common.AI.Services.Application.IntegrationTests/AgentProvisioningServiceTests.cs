using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.AI.Agents.Exceptions;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;
using GovUK.Dfe.AI.Agents.Quality;
using GovUK.Dfe.AI.Agents.ValueObjects;
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
            return new AgentResult { AgentName = call.Arg<AgentDefinition>().Name, Output = "398 pupils are on roll [Evidence 1]." };
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
    public async Task Applies_the_guardrail_before_testing_and_provisioning()
    {
        _host.AddCaseForEveryAgent();
        var events = new List<string>();
        _host.Guardrails.ApplyAsync(default).ReturnsForAnyArgs(_ =>
        {
            events.Add("guardrail");
            return new GuardrailReport(ProvisioningTestHost.GuardrailName, []);
        });
        _host.Agents.RunAsync(default!, default!, default, default).ReturnsForAnyArgs(call =>
        {
            lock (events) { events.Add("test"); }
            return new AgentResult { AgentName = call.Arg<AgentDefinition>().Name, Output = "398 pupils are on roll [Evidence 1]." };
        });

        await _host.Build().ProvisionAsync();

        Assert.Equal("guardrail", events[0]);
        Assert.Equal(1, events.Count(e => e == "guardrail"));
    }

    [Fact]
    public async Task Stops_before_testing_when_the_guardrail_is_not_in_place()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Guardrails.ApplyAsync(default).ReturnsForAnyArgs(
            new GuardrailReport(ProvisioningTestHost.GuardrailName, ["Deployment 'gpt-5.1' wasn't found"]));

        var ex = await Assert.ThrowsAsync<GuardrailsNotAppliedException>(() => _host.Build().ProvisionAsync());

        Assert.Contains("Deployment 'gpt-5.1' wasn't found", ex.Message);
        await _host.Agents.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, default);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task A_guardrail_block_passes_a_case_that_allows_it()
    {
        _host.AddCaseForEveryAgent(caseName: "injected-instruction", guardrailMayBlock: true);
        _host.Agents.RunAsync(default!, default!, default, default).ThrowsAsyncForAnyArgs(call =>
            new AgentGuardrailException(call.Arg<AgentDefinition>().Name, AgentGuardrailException.PromptStage));

        var provisioned = await _host.Build().ProvisionAsync();

        Assert.Equal(CommonAgents.All.Length, provisioned.Count);
        Assert.Contains(_host.Logs, l => l.Message.EndsWith("injected-instruction: blocked by the Foundry guardrail, which this case allows"));
    }

    [Fact]
    public async Task A_guardrail_block_fails_a_case_that_does_not_allow_it()
    {
        _host.AddCaseForEveryAgent(caseName: "ordinary-question");
        _host.Agents.RunAsync(default!, default!, default, default).ThrowsAsyncForAnyArgs(call =>
            new AgentGuardrailException(call.Arg<AgentDefinition>().Name, AgentGuardrailException.AnswerStage));

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build().ProvisionAsync());

        Assert.Contains("Case ordinary-question: The run failed: A Foundry guardrail blocked the answer", ex.Message);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
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

        await _host.Runtime.Received(1).DeleteOrphanedEphemeralAgentsAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deletes_leftover_test_agents_when_the_gate_throws()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Judge.EvaluateAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("Judge failed."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.Build(JudgeModel).ProvisionAsync());

        await _host.Runtime.Received(1).DeleteOrphanedEphemeralAgentsAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_clean_up_does_not_block_release()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Runtime.DeleteOrphanedEphemeralAgentsAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
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
        Assert.Contains(FormattableString.Invariant($"{JudgeMetrics.All[0]}: averaged 2, below the minimum {minimum}"), ex.Message);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Runs_each_test_case_the_configured_number_of_times()
    {
        _host.AddCaseForEveryAgent(mustMention: ["398"]);
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");

        await _host.Build(repeats: 3).ProvisionAsync();

        await _host.Agents.ReceivedWithAnyArgs(AgentNames.All.Count * 3).RunAsync(default!, default!, default, default);
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
            Assert.Contains($"{metric}: not scored", ex.Message);
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
            JudgeMetrics.All.ToDictionary(metric => metric, _ => call.Arg<CompletedAgentRun>().Prompt == Weather ? 1.0 : 5.0));

        var provisioned = await _host.Build(JudgeModel).ProvisionAsync();

        Assert.Equal(CommonAgents.All.Length, provisioned.Count);
        Assert.Contains(_host.Logs, l => l.Message.EndsWith("(1 cases expecting a refusal not scored)"));
    }

    [Fact]
    public async Task Blocks_release_when_one_group_of_cases_scores_much_worse_than_another()
    {
        const string SpecialSchool = "What is the capacity of the special school?";
        _host.AddCaseForEveryAgent(caseName: "academy", group: "academy");
        _host.AddCaseForEveryAgent(caseName: "special-school", prompt: SpecialSchool, group: "special-school");
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Judge.EvaluateAsync(default!, default).ReturnsForAnyArgs(call =>
            JudgeMetrics.All.ToDictionary(metric => metric, _ => call.Arg<CompletedAgentRun>().Prompt == SpecialSchool ? 3.6 : 4.8));

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build(JudgeModel, maxGroupGap: 0.5).ProvisionAsync());

        Assert.Contains($"{JudgeMetrics.All[0]}: special-school 3.6, academy 4.8: a gap over 0.5", ex.Message);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Blocks_release_when_scores_fall_below_the_baseline_by_more_than_the_tolerance()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.SaveBaselineForEveryAgent(score: 4.8);
        _host.JudgeScores(4.2);

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build(JudgeModel).ProvisionAsync());

        Assert.Contains($"{JudgeMetrics.All[0]}: fell from 4.8 to 4.2, more than the tolerance 0.2", ex.Message);
    }

    [Fact]
    public async Task Provisions_when_scores_stay_within_the_tolerance_of_the_baseline()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.SaveBaselineForEveryAgent(score: 4.8);
        _host.JudgeScores(4.7);

        var provisioned = await _host.Build(JudgeModel).ProvisionAsync();

        Assert.Equal(CommonAgents.All.Length, provisioned.Count);
    }

    [Fact]
    public async Task An_unreadable_baseline_is_invalid_configuration()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.JudgeScores(4.5);
        _host.SaveUnreadableBaseline(AgentNames.Trust);

        var ex = await Assert.ThrowsAsync<AgentConfigurationException>(() => _host.Build(JudgeModel).ProvisionAsync());

        Assert.Contains($"{AgentNames.Trust}.json isn't a readable evaluation report", ex.Message);
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
