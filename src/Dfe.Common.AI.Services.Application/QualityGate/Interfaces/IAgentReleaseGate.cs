using Dfe.Common.AI.Services.Application.ValueObjects;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.QualityGate.Interfaces;

/// <summary>
/// Evaluates whether a set of agents can be released to production by running their test cases and checking their average judge scores against the minimum required.
/// </summary>
public interface IAgentReleaseGate
{
    /// <summary>
    /// Runs each agent's test cases on a temporary copy of the agent, so no Foundry version is created. An agent
    /// passes when every case passes and its average judge scores meet the minimum.
    /// </summary>
    /// <param name="definitions">The agent definitions to evaluate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results of the evaluation.</returns>
    Task<IReadOnlyList<AgentGateResult>> EvaluateAsync(IReadOnlyCollection<AgentDefinition> definitions, CancellationToken cancellationToken = default);
}
