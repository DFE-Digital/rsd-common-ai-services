using System.Text.Json;
using Dfe.Common.AI.Services.Application;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var errorLogging = LoggerFactory.Create(logging => logging.AddSimpleConsole(o => o.SingleLine = true));
var errorLogger = errorLogging.CreateLogger<Program>();

try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Configuration.AddUserSecrets<Program>(optional: true);

    builder.Services.AddAgentProvisioning(builder.Configuration);

    using var host = builder.Build();
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
    return (int)ErrorHandler.Handle(ex, errorLogger);
}
