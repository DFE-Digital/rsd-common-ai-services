using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Dfe.Common.AI.Services.Application.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void Sends_telemetry_to_Application_Insights_when_a_connection_string_is_set()
    {
        var settings = ProvisioningTestHost.RequiredSettings();
        settings["AiAgents:RequireTokenUsageTelemetry"] = "true";
        settings[ObservabilityExtensions.ConnectionStringKey] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost/";

        var services = new ServiceCollection().AddAgentProvisioning(Configuration(settings));

        Assert.Contains(services, s => s.ServiceType == typeof(MeterProvider));
        Assert.Contains(services, s => s.ServiceType == typeof(TracerProvider));
    }

    [Fact]
    public void Logs_go_through_Serilog_to_Application_Insights_only()
    {
        var settings = ProvisioningTestHost.RequiredSettings();
        settings[ObservabilityExtensions.ConnectionStringKey] =
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
