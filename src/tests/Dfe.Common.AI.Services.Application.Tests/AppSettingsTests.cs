using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Interfaces;
using GovUK.Dfe.AI.Agents.Safety.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dfe.Common.AI.Services.Application.Tests.Helpers;

namespace Dfe.Common.AI.Services.Application.Tests;

/// <summary>What the App's own appsettings.json turns on.</summary>
public sealed class AppSettingsTests
{
    [Fact]
    public void Input_is_checked_before_it_reaches_an_agent()
    {
        // Foundry's guardrail doesn't run for a model behind API Management, nor scan any agent's evidence, so the app
        // checks the question and evidence itself: without this, the guardrail/ test cases can't pass.
        var appSettings = Path.Combine(Repository.Root(), "src", "Dfe.Common.Ai.Services.App", "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettings)
            .AddInMemoryCollection(ProvisioningTestHost.RequiredSettings())
            .Build();

        var services = new ServiceCollection().AddAgentProvisioning(configuration);

        Assert.Contains(services, service => service.ServiceType == typeof(IInputShield));
    }

    [Fact]
    public void The_release_is_decided_by_the_quality_gate_with_a_valid_policy()
    {
        // The library registers the quality gate only when its policy under AiAgents:Evaluation:QualityGate is valid.
        var appSettings = Path.Combine(Repository.Root(), "src", "Dfe.Common.Ai.Services.App", "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettings)
            .AddInMemoryCollection(ProvisioningTestHost.RequiredSettings())
            .Build();

        var services = new ServiceCollection().AddAgentProvisioning(configuration);

        Assert.Contains(services, service => service.ServiceType == typeof(IResponsibleAiQualityGate));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, configuration["AiAgents:Evaluation:QualityGate:GovernanceFile"]!)),
            "The governance file the policy names must be copied to the output.");
    }
}
