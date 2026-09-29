using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents.Interfaces;

/// <summary>
/// Provision agents by running their release checks and creating Foundry versions for those that pass. If any agent fails, nothing is provisioned.
/// </summary>
public interface IAgentProvisioningService
{
    /// <summary>
    /// Provisions the agents by running their release checks and creating Foundry versions for those that pass. If any agent fails, nothing is provisioned.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provisioned agent references.</returns>
    Task<IReadOnlyList<AgentReference>> ProvisionAsync(CancellationToken cancellationToken = default);
}
