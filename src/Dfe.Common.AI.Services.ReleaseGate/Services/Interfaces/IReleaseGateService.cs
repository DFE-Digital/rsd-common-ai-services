using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.ReleaseGate.Services.Interfaces;

/// <summary>
/// Decides whether agents can be released: the Foundry guardrail must be in place, then every agent must pass its
/// guardrail and evaluation test cases.
/// </summary>
public interface IReleaseGateService
{
    /// <summary>Returns when every agent may be released; otherwise throws, and nothing should be provisioned.</summary>
    /// <exception cref="Exceptions.GuardrailNotInPlaceException">The guardrail is missing or weaker than the settings.</exception>
    /// <exception cref="Exceptions.AgentReleaseBlockedException">An agent failed, or wasn't tested.</exception>
    Task CheckAsync(IReadOnlyList<AgentDefinition> definitions, CancellationToken cancellationToken = default);
}
