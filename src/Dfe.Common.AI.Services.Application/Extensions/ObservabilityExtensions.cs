using Azure.Monitor.OpenTelemetry.Exporter;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Diagnostics;
using Dfe.Common.AI.Services.Application.Exceptions;
using GovUK.Dfe.AI.Agents.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using Serilog;

namespace Dfe.Common.AI.Services.Application.Extensions;

internal static class ObservabilityExtensions
{
    /// <summary>Read by the Azure Monitor exporter itself, from settings or the environment variable of that name.</summary>
    internal const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    private const string ApplicationNameKey = "AiAgents:ApplicationName";
    private const string RequireTelemetryKey = "AiAgents:RequireTokenUsageTelemetry";

    /// <summary>
    /// Sends logs, traces and metrics to Application Insights only. Serilog, configured by the <c>Serilog</c> settings,
    /// replaces the host's logging providers (such as the console) and passes its events to the OpenTelemetry provider,
    /// so logs share the exporter with the agents library's metrics and traces and the release-gate metrics.
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

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(applicationName))
            .WithTracing(tracing => tracing.AddSource(AgentTelemetry.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(AgentTelemetry.SourceName, ReleaseGateMetrics.MeterName))
            .WithLogging(configureBuilder: null, configureOptions: logs => logs.IncludeFormattedMessage = true)
            .UseAzureMonitorExporter();

        return services;
    }
}
