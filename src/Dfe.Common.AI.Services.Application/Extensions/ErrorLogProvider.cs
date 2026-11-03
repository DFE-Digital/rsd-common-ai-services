using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Dfe.Common.AI.Services.Application.Extensions;

/// <summary>
/// Sends Error and Critical logs, with their exceptions, to Application Insights, where they're kept after the console
/// has gone. Serilog passes every event here, so the level is filtered by this provider's own factory.
/// </summary>
internal sealed class ErrorLogProvider(string connectionString, string applicationName) : ILoggerProvider
{
    private readonly ILoggerFactory _factory = LoggerFactory.Create(logging => logging
        .SetMinimumLevel(LogLevel.Error)
        .AddOpenTelemetry(otel =>
        {
            otel.IncludeFormattedMessage = true;
            otel.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(applicationName));
            otel.AddAzureMonitorLogExporter(exporter => exporter.ConnectionString = connectionString);
        }));

    public ILogger CreateLogger(string categoryName) => _factory.CreateLogger(categoryName);

    /// <summary>Flushes the errors still waiting to be sent.</summary>
    public void Dispose() => _factory.Dispose();
}
