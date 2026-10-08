using Azure.Monitor.OpenTelemetry.Exporter;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Diagnostics;
using Dfe.Common.AI.Services.Application.Exceptions;
using GovUK.Dfe.AI.Agents.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using Serilog;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.Extensions;

[ExcludeFromCodeCoverage]
public static class ApplicationInsightExtension
{
    private const string ApplicationNameKey = "AiAgents:ApplicationName";
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    private const string RequireTelemetryKey = "AiAgents:RequireTokenUsageTelemetry";

    /// <summary>
    /// Adds Application Insights telemetry to the application.
    /// </summary>
    /// <param name="builder"></param>
    /// <exception cref="AgentConfigurationException"></exception>
    public static IServiceCollection AddApplicationInsights(this IServiceCollection services, IConfiguration configuration)
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