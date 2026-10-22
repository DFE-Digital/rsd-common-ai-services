using Azure.Monitor.OpenTelemetry.Exporter;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using GovUK.Dfe.AI.Agents.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using Serilog;
using Dfe.Common.AI.Services.ReleaseGate.Metrics;

namespace Dfe.Common.AI.Services.Application.Extensions;

internal static class ObservabilityExtensions
{
    /// <summary>Read by the Azure Monitor exporter itself, from settings or the environment variable of that name.</summary>
    internal const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    private const string ApplicationNameKey = "AiAgents:ApplicationName";
    private const string RequireTelemetryKey = "AiAgents:RequireTokenUsageTelemetry";

    /// <summary>
    /// Sends traces, metrics (tokens, cost, release-gate results) and error logs to Application Insights. Every log goes
    /// where the <c>Serilog</c> settings send it (the console in appsettings.json). Serilog replaces the host's own
    /// logging providers, and passes every event on to the providers registered here: the error log provider, and
    /// tests' fake logger.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The updated service collection.</returns>
    /// <exception cref="AgentConfigurationException">
    /// No connection string is set while <c>AiAgents:RequireTokenUsageTelemetry</c> is true (the default).
    /// </exception>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSerilog(
            serilog => serilog.ReadFrom.Configuration(configuration),
            preserveStaticLogger: true,
            writeToProviders: true);

        if (string.IsNullOrWhiteSpace(configuration[ConnectionStringKey]))
        {
            if (configuration.GetValue(RequireTelemetryKey, defaultValue: true))
            {
                throw new AgentConfigurationException(Messages.Errors.ApplicationInsightsMissing);
            }

            return services;
        }

        // The role name in Application Insights; the library tags its metrics with the same ApplicationName.
        var applicationName = configuration[ApplicationNameKey] ?? AppDomain.CurrentDomain.FriendlyName;

        // Not UseAzureMonitorExporter, which would send every log there too, not only errors.
        var connectionString = configuration[ConnectionStringKey]!;
        services.AddSingleton<ILoggerProvider>(_ => new ErrorLogProvider(connectionString, applicationName));
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(applicationName))
            .WithTracing(tracing => tracing.AddSource(AgentTelemetry.SourceName)
                .AddAzureMonitorTraceExporter(exporter => exporter.ConnectionString = connectionString))
            .WithMetrics(metrics => metrics.AddMeter(AgentTelemetry.SourceName, ReleaseGateMetrics.MeterName)
                .AddAzureMonitorMetricExporter(exporter => exporter.ConnectionString = connectionString));

        return services;
    }
}
