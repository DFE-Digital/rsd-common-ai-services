using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Enums;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Dfe.Common.AI.Services.Application.Tests.Helpers;

namespace Dfe.Common.AI.Services.Application.Tests.Extensions;

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
    public void Only_errors_are_logged_to_Application_Insights()
    {
        var settings = ProvisioningTestHost.RequiredSettings();
        settings[ObservabilityExtensions.ConnectionStringKey] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost/";
        var services = new ServiceCollection().AddSingleton<ILoggerProvider, ConsoleStandIn>();

        services.AddAgentProvisioning(Configuration(settings));

        // The host's default providers are gone; the only one left sends errors to Application Insights.
        Assert.Contains(services, s => s.ServiceType == typeof(ILoggerFactory) && s.ImplementationFactory is not null);
        var provider = Assert.Single(services, s => s.ServiceType == typeof(ILoggerProvider));
        using var errorLogs = (ILoggerProvider)provider.ImplementationFactory!(new ServiceCollection().BuildServiceProvider());
        var logger = errorLogs.CreateLogger("Test");
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.False(logger.IsEnabled(LogLevel.Warning));
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
