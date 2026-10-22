using Dfe.Common.AI.Services.ReleaseGate.Constants;
using Dfe.Common.AI.Services.ReleaseGate.Exceptions;
using Dfe.Common.AI.Services.ReleaseGate.Services;
using Dfe.Common.AI.Services.ReleaseGate.Tests.Commons;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.Enums;
using GovUK.Dfe.AI.Agents.Guardrails.Options;
using GovUK.Dfe.AI.Agents.Guardrails.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace Dfe.Common.AI.Services.ReleaseGate.Tests.Services;

public sealed class ReleaseGateServiceTests
{
    private const string Establishment = "rsd-establishment-agent";
    private const string Trust = "rsd-trust-agent";
    private const string Guardrail = "test-guardrail";
    private const string GuardrailNotBlocked = "Case guardrail/jailbreak-prompt: Not blocked by the guardrail.";
    private const string LowRelevance = "Relevance: averaged 3.2, below the minimum 3.5";

    private static readonly AgentDefinition[] Agents = [new(Establishment, "Establishment"), new(Trust, "Trust")];

    private readonly IAgentReleaseGate _gate = Substitute.For<IAgentReleaseGate>();
    private readonly IFoundryGuardrailsService _guardrails = Substitute.For<IFoundryGuardrailsService>();
    private readonly FakeLogger<ReleaseGateService> _logger = new();

    public ReleaseGateServiceTests()
    {
        GuardrailHas();
        GateReturns(Passed(Establishment), Passed(Trust));
    }

    [Fact]
    public async Task Checks_the_guardrail_before_testing_any_agent()
    {
        await Service().CheckAsync(Agents);

        Received.InOrder(() =>
        {
            _guardrails.CheckAsync(Arg.Any<CancellationToken>());
            _gate.EvaluateAsync(Arg.Any<IReadOnlyCollection<AgentDefinition>>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Tests_exactly_the_agents_it_is_given_and_releases_them_when_every_section_passes()
    {
        await Service().CheckAsync(Agents);

        await _gate.Received(1).EvaluateAsync(
            Arg.Is<IReadOnlyCollection<AgentDefinition>>(tested => tested.SequenceEqual(Agents)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stops_before_testing_when_the_guardrail_is_not_in_place_and_says_why()
    {
        GuardrailHas("blocklist 'personal-identifiers' doesn't exist");

        var ex = await Assert.ThrowsAsync<GuardrailNotInPlaceException>(() => Service().CheckAsync(Agents));

        Assert.Contains("blocklist 'personal-identifiers' doesn't exist", ex.Message, StringComparison.Ordinal);
        await _gate.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default);
    }

    [Fact]
    public async Task Blocks_an_agent_the_gate_returned_no_result_for_as_it_was_not_tested()
    {
        GateReturns(Passed(Establishment));

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => Service().CheckAsync(Agents));

        var notTested = Assert.Single(ex.Failed);
        Assert.Equal(Trust, notTested.AgentName);
        Assert.Equal([Messages.Errors.NotTested], notTested.Problems);
    }

    [Fact]
    public async Task Blocks_only_the_failing_agents_with_each_problem_marked_with_its_section()
    {
        GateReturns(Passed(Establishment), new AgentGateResult(Trust, [LowRelevance, GuardrailNotBlocked]));

        var ex = await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => Service().CheckAsync(Agents));

        Assert.Equal([Trust], ex.Failed.Select(result => result.AgentName));
        Assert.Contains($"- {Trust} [guardrail]: {GuardrailNotBlocked}", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"- {Trust} [evaluation]: {LowRelevance}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_each_agents_sections_and_a_blocked_release_as_an_error_for_Application_Insights()
    {
        GateReturns(Passed(Establishment), new AgentGateResult(Trust, [GuardrailNotBlocked]));

        await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => Service().CheckAsync(Agents));

        // Only errors reach Application Insights, so the blocked release must be one; the rest are console progress.
        var logs = _logger.Collector.GetSnapshot().Select(log => (log.Level, log.Message)).ToList();
        Assert.Contains((LogLevel.Information, $"Foundry guardrail {Guardrail} is in place"), logs);
        Assert.Contains((LogLevel.Information, $"{Establishment}: guardrail passed, evaluation passed"), logs);
        Assert.Contains((LogLevel.Warning, $"{Trust}: guardrail failed (1 problems), evaluation passed"), logs);
        Assert.Contains((LogLevel.Error, $"Agent release checks failed for 1 agents: {Trust}"), logs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Records_each_agents_guardrail_check_for_the_release_query(bool inPlace)
    {
        if (!inPlace)
        {
            GuardrailHas("the guardrail doesn't exist");
        }

        using var recorded = new GuardrailCheckListener();
        var check = Service().CheckAsync(Agents);
        await (inPlace ? check : Assert.ThrowsAsync<GuardrailNotInPlaceException>(() => check));

        var outcome = inPlace ? "passed" : "failed";
        Assert.Contains((Establishment, outcome), recorded.Checks);
        Assert.Contains((Trust, outcome), recorded.Checks);
    }

    [Fact]
    public async Task Passes_cancellation_on_to_the_guardrail_check_and_the_gate()
    {
        using var cancellation = new CancellationTokenSource();

        await Service().CheckAsync(Agents, cancellation.Token);

        await _guardrails.Received(1).CheckAsync(cancellation.Token);
        await _gate.Received(1).EvaluateAsync(Arg.Any<IReadOnlyCollection<AgentDefinition>>(), cancellation.Token);
    }

    private ReleaseGateService Service() =>
        new(_gate, _guardrails, new GuardrailSettings { Name = Guardrail, Targets = GuardrailTargets.Agents }, _logger);

    private void GuardrailHas(params string[] problems) =>
        _guardrails.CheckAsync(default).ReturnsForAnyArgs(new GuardrailReport(Guardrail, problems));

    private void GateReturns(params AgentGateResult[] results) =>
        _gate.EvaluateAsync(default!, default).ReturnsForAnyArgs(results);

    private static AgentGateResult Passed(string agentName) => new(agentName, []);
}
