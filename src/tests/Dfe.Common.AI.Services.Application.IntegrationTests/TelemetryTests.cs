using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Diagnostics;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Extensions;
using Dfe.Common.AI.Services.Application.QualityGate;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

/// <summary>Not run alongside other tests, whose release-gate metrics would be collected too.</summary>
[CollectionDefinition(nameof(TelemetryTests), DisableParallelization = true)]
[Collection(nameof(TelemetryTests))]
public sealed class TelemetryTests : IDisposable
{
    private const string AgentNameTag = "gen_ai.agent.name";
    private const string OutcomeTag = "dfe.release_gate.outcome";

    private readonly ProvisioningTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Sends_telemetry_to_Application_Insights_when_a_connection_string_is_set()
    {
        var settings = ProvisioningTestHost.RequiredSettings();
        settings["AiAgents:RequireTokenUsageTelemetry"] = "true";
        settings[ApplicationInsightExtension.ConnectionStringKey] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost/";

        var services = new ServiceCollection().AddAgentProvisioning(Configuration(settings));

        Assert.Contains(services, s => s.ServiceType == typeof(MeterProvider));
        Assert.Contains(services, s => s.ServiceType == typeof(TracerProvider));
    }

    [Fact]
    public void Logs_go_through_Serilog_to_Application_Insights_only()
    {
        var settings = ProvisioningTestHost.RequiredSettings();
        settings[ApplicationInsightExtension.ConnectionStringKey] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost/";
        var services = new ServiceCollection().AddSingleton<ILoggerProvider, ConsoleStandIn>();

        services.AddAgentProvisioning(Configuration(settings));

        Assert.DoesNotContain(services, s => s.ImplementationType == typeof(ConsoleStandIn));
        Assert.Contains(services, s => s.ServiceType == typeof(ILoggerProvider));
        Assert.Contains(services, s => s.ServiceType == typeof(ILoggerFactory) && s.ImplementationFactory is not null);
    }

    [Fact]
    public void A_missing_connection_string_is_invalid_configuration_while_telemetry_is_required()
    {
        var settings = ProvisioningTestHost.RequiredSettings();
        settings["AiAgents:RequireTokenUsageTelemetry"] = "true";

        var ex = Assert.Throws<AgentConfigurationException>(() =>
            new ServiceCollection().AddAgentProvisioning(Configuration(settings)));

        Assert.Equal(Messages.Errors.ApplicationInsightsMissing, ex.Message);
        Assert.Equal(ExitCode.InvalidConfiguration, ErrorHandler.Handle(ex, NullLogger.Instance));
    }

    [Fact]
    public async Task Records_release_gate_results_as_metrics()
    {
        _host.AddCaseForEveryAgent(mustMention: ["398"], caseName: "passes");
        _host.AddCaseForEveryAgent(mustMention: ["999"], caseName: "fails");
        _host.AnswerWith("398 pupils are on roll [Evidence 1].\n\nSources:\n[Evidence 1] Oakfield Primary School");
        _host.JudgeScores(4.25);
        using var testCases = new MetricCollector<long>(ReleaseGateMetrics.TestCases);
        using var scores = new MetricCollector<double>(ReleaseGateMetrics.Scores);
        using var agents = new MetricCollector<long>(ReleaseGateMetrics.Agents);

        await Assert.ThrowsAsync<AgentReleaseBlockedException>(() => _host.Build("judge-model").ProvisionAsync());

        foreach (var agent in AgentNames.All)
        {
            Assert.Equal(1, Count(testCases, agent, ReleaseGateMetrics.Passed));
            Assert.Equal(1, Count(testCases, agent, ReleaseGateMetrics.Failed));
            Assert.Equal(1, Count(agents, agent, ReleaseGateMetrics.Failed));
            Assert.Equal(
                JudgeMetrics.All.Order(),
                scores.GetMeasurementSnapshot()
                    .Where(m => Equals(m.Tags[AgentNameTag], agent) && m.Value == 4.25)
                    .Select(m => (string)m.Tags["gen_ai.evaluation.name"]!)
                    .Order());
        }
    }

    private static long Count(MetricCollector<long> collector, string agent, string outcome) =>
        collector.GetMeasurementSnapshot()
            .Where(m => Equals(m.Tags[AgentNameTag], agent) && Equals(m.Tags[OutcomeTag], outcome))
            .Sum(m => m.Value);

    /// <summary>Stands in for a provider the host adds by default, such as the console.</summary>
    private sealed class ConsoleStandIn : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
            // Nothing to release.
        }
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
