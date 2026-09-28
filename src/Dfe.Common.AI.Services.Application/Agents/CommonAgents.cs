using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// Provisioned agents that are common to all DfE's RSD services, with their system prompts and other settings.
/// </summary>
public static class CommonAgents
{
    public static readonly AgentDefinition Establishment = new("establishment-agent", SystemPromptType: "Establishment");

    public static readonly AgentDefinition Ofsted = new("ofsted-agent", SystemPromptType: "Ofsted");

    public static readonly AgentDefinition Trust = new("trust-agent", SystemPromptType: "Trust");

    public static AgentDefinition[] All => [Establishment, Ofsted, Trust];
}
