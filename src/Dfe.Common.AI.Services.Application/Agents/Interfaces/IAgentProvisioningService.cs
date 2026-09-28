using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents.Interfaces;

/// <summary>
/// Provisions managed agents in Foundry and responsible for creating or reusing agent versions based on their prompts, tools, and schemas, without running the agents themselves.
/// </summary>
public interface IAgentProvisioningService
{
    /// <summary>
    /// Creates each managed agent in Foundry, or reuses its current version when the prompt, tools and
    /// schema are unchanged. No agent is run.
    /// </summary>
    /// <returns>The agent versions for consuming apps to pin.</returns>
    Task<IReadOnlyList<AgentReference>> ProvisionAsync(CancellationToken cancellationToken = default);
}
