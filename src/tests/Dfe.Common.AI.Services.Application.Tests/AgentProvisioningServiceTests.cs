using Azure;
using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.Exceptions;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;
using GovUK.Dfe.AI.Agents.Quality;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.Extensions;
using GovUK.Dfe.AI.Agents.Evaluation.Exceptions;
using GovUK.Dfe.AI.Agents.Evaluation.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Tests;

public sealed class AgentProvisioningServiceTests : IDisposable
{
    private const string JudgeModel = "tests/judge-model";

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
            Arg.Is<AgentDefinition>(d => d.IsManagedAgent), Arg.Any<string>(), Arg.Any<AgentEvidence?>(), Arg.Any<CancellationToken>());
        await _host.Agents.Received(CommonAgents.All.Length).ProvisionAsync(
            Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => d.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_the_new_versions_when_provisioning_stops_part_way()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Agents.Configure().ProvisionAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            var definition = call.Arg<IReadOnlyCollection<AgentDefinition>>().Single();
            return definition.Name == AgentNames.Trust
                ? Task.FromException<IReadOnlyList<AgentReference>>(new RequestFailedException(403, "Forbidden."))
                : Task.FromResult<IReadOnlyList<AgentReference>>([new AgentReference(definition.Name, definition.Name, "2")]);
        });

        var ex = await Assert.ThrowsAsync<AgentProvisioningIncompleteException>(() => _host.Build().ProvisionAsync());

        Assert.Equal([AgentNames.Establishment], ex.Provisioned.Select(a => a.Name));
        Assert.Equal([AgentNames.Trust], ex.Failed);
        Assert.Contains($"Foundry has new versions of {AgentNames.Establishment} 2", ex.Message);
        Assert.IsType<RequestFailedException>(ex.InnerException);
    }

    [Fact]
    public async Task Reports_the_original_failure_when_no_agent_was_provisioned()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Agents.Configure().ProvisionAsync(default!, default).ThrowsAsyncForAnyArgs(new RequestFailedException(403, "Forbidden."));

        await Assert.ThrowsAsync<RequestFailedException>(() => _host.Build().ProvisionAsync());
    }

    [Fact]
    public async Task Checks_the_guardrail_before_testing_and_never_changes_it()
    {
        _host.AddCaseForEveryAgent();
        var events = new List<string>();
        _host.Guardrails.CheckAsync(default).ReturnsForAnyArgs(_ =>
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
        await _host.Guardrails.DidNotReceiveWithAnyArgs().ApplyAsync(default);
    }

    [Fact]
    public async Task Stops_before_testing_when_the_guardrail_is_not_in_place()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.Guardrails.CheckAsync(default).ReturnsForAnyArgs(
            new GuardrailReport(ProvisioningTestHost.GuardrailName, ["blocklist 'personal-identifiers' doesn't exist"]));

        var ex = await Assert.ThrowsAsync<GuardrailNotInPlaceException>(() => _host.Build().ProvisionAsync());

        Assert.Contains("blocklist 'personal-identifiers' doesn't exist", ex.Message);
        await _host.Agents.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, default);
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
    public async Task Blocks_release_when_the_guardrail_blocks_a_case_that_should_be_answered()
    {
        _host.AddCaseForEveryAgent();
        _host.Agents.RunAsync(default!, default!, default, default).ThrowsAsyncForAnyArgs(
            call => new AgentGuardrailException(call.Arg<AgentDefinition>().Name, "prompt"));

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build().ProvisionAsync());

        Assert.Equal(CommonAgents.All.Length, ex.Failed.Count);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Records_each_agents_guardrail_check_and_whether_a_version_was_created(bool passes)
    {
        _host.AddCaseForEveryAgent(mustNotMention: passes ? [] : ["398"]);
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        var recorded = new System.Collections.Concurrent.ConcurrentBag<(string Metric, string Agent, string? Value)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Dfe.Common.AI.Services")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var tagList = tags.ToArray();
            string? Tag(string key) => tagList.FirstOrDefault(t => t.Key == key).Value as string;
            recorded.Add((instrument.Name, Tag("gen_ai.agent.name")!, Tag("dfe.ai_agents.release_gate.outcome") ?? Tag("gen_ai.agent.version")));
        });
        listener.Start();

        try
        {
            await _host.Build().ProvisionAsync();
        }
        catch (AgentReleaseBlockedException) when (!passes)
        {
            // Expected: the answer mentions a forbidden fact.
        }

        foreach (var agent in AgentNames.All)
        {
            Assert.Contains(("dfe.common_ai_services.guardrail.checks", agent, "passed"), recorded);
            Assert.Equal(passes, recorded.Contains(("dfe.common_ai_services.agents.provisioned", agent, "1")));
        }
    }

    [Fact]
    public async Task Provisions_nothing_when_the_gate_returns_no_result_for_an_agent()
    {
        var gate = Substitute.For<IAgentReleaseGate>();
        gate.EvaluateAsync(default!, default).ReturnsForAnyArgs([new AgentGateResult(AgentNames.Establishment, [])]);
        _host.ReleaseGate = gate;

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build().ProvisionAsync());

        var notTested = Assert.Single(ex.Failed);
        Assert.Equal(AgentNames.Trust, notTested.AgentName);
        Assert.Equal([Messages.Errors.NotTested], notTested.Problems);
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Blocks_release_when_judge_scores_are_below_the_minimum()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.JudgeScores(2.0);

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build(JudgeModel).ProvisionAsync());

        Assert.Contains($"{JudgeMetrics.Groundedness}: averaged 2, below the minimum 3.5", ex.Message);
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
    public async Task An_unreadable_baseline_is_invalid_configuration()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith("398 pupils are on roll [Evidence 1].");
        _host.JudgeScores(4.5);
        _host.SaveUnreadableBaseline(AgentNames.Trust);

        var ex = await Assert.ThrowsAsync<ReleaseGateException>(() => _host.Build(JudgeModel).ProvisionAsync());

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
