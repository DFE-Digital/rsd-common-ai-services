using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// Provisioned agents that are common to all DfE's RSD services, with their system prompts and other settings.
/// </summary>
/// <remarks>
/// <see cref="AgentDefinition.RequireCitations"/> and <see cref="AgentDefinition.Validate"/> are applied when an
/// agent runs, not stored in its Foundry version, so apps that run these agents must use these definitions.
/// </remarks>
public static class CommonAgents
{
    public static AgentDefinition[] All =>
    [
        Grounded("rsd-establishment-agent", systemPromptKey: "Establishment"),
        Grounded("rsd-ofsted-agent", systemPromptKey: "Ofsted"),
        Grounded("rsd-trust-agent", systemPromptKey: "Trust"),
    ];

    /// <summary>
    /// An agent that must cite its evidence (the library's <c>RequiredCitations</c>, on by default), list the sources
    /// it cited, and can't give an empty answer.
    /// </summary>
    private static AgentDefinition Grounded(string name, string systemPromptKey) =>
        new(name, systemPromptKey) { Validate = AnswerChecks.Check };
}
