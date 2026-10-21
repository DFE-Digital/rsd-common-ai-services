using System.Diagnostics.Metrics;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// The job's own release metrics, tagged like the library's release-gate metrics, so one query shows each agent's
/// guardrail check, release checks and whether a version was created (see README).
/// </summary>
internal static class ReleaseMetrics
{
    public const string MeterName = "Dfe.Common.AI.Services";

    private const string AgentNameTag = "gen_ai.agent.name";
    private const string OutcomeTag = "dfe.ai_agents.release_gate.outcome";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> GuardrailChecks = Meter.CreateCounter<long>(
        "dfe.common_ai_services.guardrail.checks", "{agent}", "Agents whose Foundry guardrail was checked, by outcome.");

    private static readonly Counter<long> Provisioned = Meter.CreateCounter<long>(
        "dfe.common_ai_services.agents.provisioned", "{agent}", "Agent versions created or reused in Foundry, by agent and version.");

    public static void RecordGuardrailCheck(IEnumerable<AgentDefinition> agents, bool passed)
    {
        foreach (var agent in agents)
        {
            GuardrailChecks.Add(1, new(AgentNameTag, agent.Name), new(OutcomeTag, passed ? "passed" : "failed"));
        }
    }

    public static void RecordProvisioned(IEnumerable<AgentReference> agents)
    {
        foreach (var agent in agents)
        {
            Provisioned.Add(1, new(AgentNameTag, agent.Name), new("gen_ai.agent.version", agent.Version));
        }
    }
}
