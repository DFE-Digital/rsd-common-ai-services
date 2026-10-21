using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// Provisioned agents that are common to all DfE's RSD services, with their system prompts and other settings.
/// </summary>
/// <remarks>
/// <see cref="AgentDefinition.RequiredCitations"/> and <see cref="AgentDefinition.Validate"/> are applied when an
/// agent runs, not stored in its Foundry version, so apps that run these agents must use these definitions. An app adds
/// its own checks with <see cref="AgentChecks.WithCheck"/> or <see cref="AgentChecks.ExpectingMention"/>, never by
/// replacing <see cref="AgentDefinition.Validate"/>, which would drop the common checks.
/// </remarks>
public static class CommonAgents
{
    /// <summary>Answers questions about schools and other establishments, and their Ofsted inspections.</summary>
    public static AgentDefinition Establishment => Grounded("rsd-establishment-agent", systemPromptKey: "Establishment");

    /// <summary>Answers questions about academy trusts and the establishments they run.</summary>
    public static AgentDefinition Trust => Grounded("rsd-trust-agent", systemPromptKey: "Trust");

    public static AgentDefinition[] All => [Establishment, Trust];

    /// <summary>
    /// An agent that must cite its evidence (the library's <c>RequiredCitations</c>, on by default), list the sources
    /// it cited, and can't give an empty answer.
    /// </summary>
    private static AgentDefinition Grounded(string name, string systemPromptKey) =>
        new(name, systemPromptKey) { Validate = AnswerChecks.Check };
}
