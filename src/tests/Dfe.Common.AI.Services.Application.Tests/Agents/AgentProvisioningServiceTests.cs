using Azure;
using Dfe.Common.AI.Services.ReleaseGate.Exceptions;
using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using GovUK.Dfe.AI.Agents.Evaluation.Metrics;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.Exceptions;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.Extensions;
using GovUK.Dfe.AI.Agents.Evaluation.Exceptions;
using GovUK.Dfe.AI.Agents.Evaluation.ValueObjects;
using Dfe.Common.AI.Services.ReleaseGate.Models;
using Dfe.Common.AI.Services.Application.Tests.Helpers;
using GovUK.Dfe.AI.Agents.Diagnostics;

namespace Dfe.Common.AI.Services.Application.Tests.Agents;

public sealed class AgentProvisioningServiceTests : IDisposable
{
    private const string JudgeModel = "tests/judge-model";

    private const string Answer = "398 pupils are on roll [Evidence 1].";

    private readonly ProvisioningTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData(JudgeModel)]
    public async Task Provisions_every_agent_one_by_one_once_temporary_copies_pass_with_or_without_a_judge(string? judgeModel)
    {
        _host.AddCaseForEveryAgent(mustMention: ["398"]);
        _host.AnswerWith(Answer);
        _host.JudgeScores(4.5);

        var provisioned = await _host.Build(judgeModel).ProvisionAsync();

        Assert.Equal(CommonAgents.All.Select(a => a.Name), provisioned.Select(a => a.Name));
        await _host.Agents.DidNotReceive().RunAsync(
            Arg.Is<AgentDefinition>(d => d.IsManagedAgent), Arg.Any<string>(), Arg.Any<AgentEvidence?>(), Arg.Any<CancellationToken>());
        await _host.Agents.Received(CommonAgents.All.Length).ProvisionAsync(
            Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => d.Count == 1), Arg.Any<CancellationToken>());
        await _host.Judge.ReceivedWithAnyArgs(judgeModel is null ? 0 : CommonAgents.All.Length).EvaluateAsync(default!, default);

        // The guardrail is created in the Foundry portal: the job only checks it, so it needs only Reader.
        await _host.Guardrails.DidNotReceiveWithAnyArgs().ApplyAsync(default);
    }

    [Fact]
    public async Task Reports_the_new_versions_when_provisioning_stops_part_way()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith(Answer);
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
        _host.AnswerWith(Answer);
        _host.Agents.Configure().ProvisionAsync(default!, default).ThrowsAsyncForAnyArgs(new RequestFailedException(403, "Forbidden."));

        await Assert.ThrowsAsync<RequestFailedException>(() => _host.Build().ProvisionAsync());
    }

    /// <summary>A release check that fails, as <see cref="Provisions_nothing_when_any_release_check_fails"/> sets it up.</summary>
    public enum FailingCheck { GuardrailNotInPlace, ForbiddenFact, CaseBlockedByMistake, LowJudgeScores }

    // The library tests each check; the job's part is to provision nothing, whichever fails, and say why.
    [Theory]
    [InlineData(FailingCheck.GuardrailNotInPlace, "[guardrail]: blocklist 'personal-identifiers' doesn't exist")]
    [InlineData(FailingCheck.ForbiddenFact, "[evaluation]: Case evaluation/case: Mentions \"Inadequate\"")]
    [InlineData(FailingCheck.CaseBlockedByMistake, "[guardrail]: Case evaluation/case: Blocked by the guardrail, which this case doesn't allow")]
    [InlineData(FailingCheck.LowJudgeScores, "[evaluation]: Groundedness: averaged 2, below the minimum 3.5")]
    public async Task Provisions_nothing_when_any_release_check_fails(FailingCheck check, string reason)
    {
        _host.AddCaseForEveryAgent(mustNotMention: ["Inadequate"]);
        _host.AnswerWith(check == FailingCheck.ForbiddenFact ? "The school is Inadequate [Evidence 1]." : Answer);
        _host.JudgeScores(check == FailingCheck.LowJudgeScores ? 2.0 : 4.5);
        if (check == FailingCheck.GuardrailNotInPlace)
        {
            _host.Guardrails.Configure().CheckAsync(default).ReturnsForAnyArgs(
                new GuardrailReport(ProvisioningTestHost.GuardrailName, ["blocklist 'personal-identifiers' doesn't exist"]));
        }
        else if (check == FailingCheck.CaseBlockedByMistake)
        {
            _host.Agents.Configure().RunAsync(default!, default!, default, default).ThrowsAsyncForAnyArgs(
                call => new AgentGuardrailException(call.Arg<AgentDefinition>().Name, "prompt"));
        }

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build(JudgeModel).ProvisionAsync());

        Assert.Equal(AgentNames.All, ex.Failed.Select(result => result.AgentName));
        Assert.All(AgentNames.All, agent => Assert.Contains($"- {agent} {reason}", ex.Message, StringComparison.Ordinal));
        await _host.Agents.DidNotReceiveWithAnyArgs().ProvisionAsync(default!, default);
    }

    [Fact]
    public async Task Logs_progress_for_each_agent_and_test_case()
    {
        _host.AddCaseForEveryAgent();
        _host.AnswerWith(Answer);

        await _host.Build().ProvisionAsync();

        var progress = _host.Logs.Where(l => l.Level == LogLevel.Information).Select(l => l.Message).ToList();
        foreach (var agent in AgentNames.All)
        {
            Assert.Contains(progress, m => m.StartsWith($"Testing {agent} with 1 test cases"));
            Assert.Contains($"{agent} / evaluation/case: passed", progress);
            Assert.Contains($"{agent}: guardrail passed, evaluation passed", progress);
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
        Assert.Contains(_host.Logs, l => l.Level == LogLevel.Error && l.Message.StartsWith("Agent release blocked by gate: 2 of 2 agents failed (release run "));
    }

    [Fact]
    public async Task Checks_the_guardrail_tests_and_provisions_in_one_release_run_so_one_query_shows_the_release()
    {
        // The library records the guardrail check and each version provisioned; here those services are fakes, so each
        // counts a step of its own instead, which shows the release run it was called in.
        var step = new AgentCounter("dfe.ai_agents.tests.release_step", "A release step, counted by a test.");
        using var steps = new MetricListener(step.Name);
        using var testCases = new MetricListener(ReleaseGateTelemetry.TestCasesMetric);
        _host.AddCaseForEveryAgent(mustMention: ["398"]);
        _host.AnswerWith(Answer);
        var provisioning = _host.Build();
        _host.Guardrails.Configure().CheckAsync(default).ReturnsForAnyArgs(_ =>
        {
            _host.Metrics.Count(step, AgentNames.Establishment);
            return new GuardrailReport(ProvisioningTestHost.GuardrailName, []);
        });
        _host.Agents.Configure().ProvisionAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            var definitions = call.Arg<IReadOnlyCollection<AgentDefinition>>();
            foreach (var definition in definitions)
            {
                _host.Metrics.Count(step, definition.Name);
            }

            return Task.FromResult<IReadOnlyList<AgentReference>>([.. definitions.Select(d => new AgentReference(d.Name, d.Name, "1"))]);
        });

        await provisioning.ProvisionAsync();

        var runIds = steps.Recorded.Select(tags => tags.GetValueOrDefault(ReleaseGateTelemetry.RunIdTag) as string).Distinct().ToList();
        var runId = Assert.Single(runIds);
        Assert.False(string.IsNullOrEmpty(runId));
        Assert.Equal(1 + AgentNames.All.Count, steps.Recorded.Count);   // the guardrail check, then each agent provisioned
        var released = testCases.Recorded.Where(tags => Equals(tags.GetValueOrDefault(ReleaseGateTelemetry.RunIdTag), runId)).ToList();
        Assert.Equal(AgentNames.All.Order(StringComparer.Ordinal),
            released.Select(tags => tags[AgentTelemetry.AgentNameTag]).OfType<string>().Order(StringComparer.Ordinal));
        Assert.All(released, tags => Assert.Equal(ProvisioningTestHost.ApplicationName, tags[AgentTelemetry.ApplicationTag]));
    }

    [Fact]
    public async Task Leaves_cases_expecting_a_refusal_out_of_the_judge_averages()
    {
        const string Weather = "What will the weather be like tomorrow?";
        _host.AddCaseForEveryAgent(caseName: "evaluation/answered");
        _host.AddCaseForEveryAgent(caseName: "evaluation/out-of-scope", prompt: Weather, mustMention: [FixedResponses.OutOfScope]);
        _host.AnswerWith("This is outside what I can answer. 398 pupils are on roll [Evidence 1].");
        _host.Judge.EvaluateAsync(default!, default).ReturnsForAnyArgs(call =>
            JudgeMetrics.All.ToDictionary(metric => metric, _ => call.Arg<CompletedAgentRun>().Prompt == Weather ? 1.0 : 5.0));

        var provisioned = await _host.Build(JudgeModel).ProvisionAsync();

        Assert.Equal(CommonAgents.All.Length, provisioned.Count);
        Assert.Contains(_host.Logs, l => l.Message.EndsWith("(1 cases expecting a refusal not scored)"));
    }
}
