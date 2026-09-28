using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the common managed agents and the service that provisions them in Foundry.
    /// Settings are read from the <c>AiAgents</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAgentProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAiAgents(configuration, agents => agents.AddAgents(CommonAgents.All));
        services.AddSingleton<IAgentProvisioningService, AgentProvisioningService>();

        return services;
    }
}
