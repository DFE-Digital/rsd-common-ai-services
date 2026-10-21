using GovUK.Dfe.AI.Agents.Safety.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.Application.Tests;

/// <summary>What the App's own appsettings.json turns on.</summary>
public sealed class AppSettingsTests
{
    [Fact]
    public void Input_is_checked_before_it_reaches_an_agent()
    {
        // Foundry's guardrail doesn't run for a model behind API Management, nor scan any agent's evidence, so the app
        // checks the question and evidence itself: without this, the guardrail/ test cases can't pass.
        var appSettings = Path.Combine(TelemetryTests.RepositoryRoot(), "src", "Dfe.Common.Ai.Services.App", "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettings)
            .AddInMemoryCollection(ProvisioningTestHost.RequiredSettings())
            .Build();

        var services = new ServiceCollection().AddAgentProvisioning(configuration);

        Assert.Contains(services, service => service.ServiceType == typeof(IInputShield));
    }
}
