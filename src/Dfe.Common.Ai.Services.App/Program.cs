using System.Text.Json;
using Dfe.Common.AI.Services.Application;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Enums;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

IHost? host = null;

try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Configuration.AddUserSecrets<Program>(optional: true);

    builder.Services.AddAgentProvisioning(builder.Configuration);

    host = builder.Build();
    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    var provisioned = await host.Services.GetRequiredService<IAgentProvisioningService>()
        .ProvisionAsync(lifetime.ApplicationStopping);

    // The settings a consuming app needs to run these agents at the versions just provisioned.
    var consumerSettings = new
    {
        AiAgents = new
        {
            ExternallyManagedAgents = provisioned.ToDictionary(a => a.Name, a => a.Version),
        },
    };
    Console.WriteLine(JsonSerializer.Serialize(consumerSettings, new JsonSerializerOptions { WriteIndented = true }));

    await host.StopAsync();
    return (int)ExitCode.Success;
}
catch (Exception ex)
{
    // Before the host exists there's nowhere to log to; the line below still says why.
    var logger = host?.Services.GetRequiredService<ILogger<Program>>() ?? (ILogger)NullLogger.Instance;
    var exitCode = ErrorHandler.Handle(ex, logger);

    // One line that always reaches whoever ran the job, even if logging isn't set up yet.
    await Console.Error.WriteLineAsync(ErrorHandler.Summarise(exitCode, ex));
    return (int)exitCode;
}
finally
{
    host?.Dispose();
}
