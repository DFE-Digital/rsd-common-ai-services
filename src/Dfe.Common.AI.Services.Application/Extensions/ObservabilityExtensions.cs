using Azure.Monitor.OpenTelemetry.Exporter;
using Dfe.Common.AI.Services.Application.Constants;
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

    /// <summary>
    /// Application Insights' role name: <c>AiAgents:ApplicationName</c>, else the app's own name, as the agents library
    /// works out the name it tags every metric with, so the two match.
    /// </summary>
    private static string ApplicationNameOf(IConfiguration configuration) =>
        configuration[ApplicationNameKey] is { Length: > 0 } name ? name : AppDomain.CurrentDomain.FriendlyName;

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
        var applicationName = ApplicationNameOf(configuration);

        // Not UseAzureMonitorExporter, which would send every log there too, not only errors.
        var connectionString = configuration[ConnectionStringKey]!;
        services.AddSingleton<ILoggerProvider>(_ => new ErrorLogProvider(connectionString, applicationName));
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(applicationName))
            .WithTracing(tracing => tracing.AddSource(AgentTelemetry.SourceName)
                .AddAzureMonitorTraceExporter(exporter => exporter.ConnectionString = connectionString))
            // The library's meter, which the release gate's and provisioning metrics use too.
            .WithMetrics(metrics => metrics.AddMeter(AgentTelemetry.SourceName)
                .AddAzureMonitorMetricExporter(exporter => exporter.ConnectionString = connectionString));

        return services;
    }
}
