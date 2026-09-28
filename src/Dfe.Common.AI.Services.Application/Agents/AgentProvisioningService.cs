using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.Agents;

public sealed class AgentProvisioningService(IAgentService agents, ILogger<AgentProvisioningService> logger)
    : IAgentProvisioningService
{
    public async Task<IReadOnlyList<AgentReference>> ProvisionAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Provisioning {Count} managed agents: {Agents}",
            CommonAgents.All.Length, string.Join(", ", CommonAgents.All.Select(a => a.Name)));

        var provisioned = await agents.ProvisionAsync(CommonAgents.All, cancellationToken);

        foreach (var agent in provisioned)
        {
            logger.LogInformation("Provisioned {AgentName} at version {Version} ({AgentId})",
                agent.Name, agent.Version, agent.Id);
        }

        return provisioned;
    }
}
