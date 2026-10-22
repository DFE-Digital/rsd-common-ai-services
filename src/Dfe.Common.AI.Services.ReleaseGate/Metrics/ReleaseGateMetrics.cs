using System.Diagnostics.Metrics;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.ReleaseGate.Metrics;

/// <summary>
/// The guardrail check for each agent, tagged like the library's release-gate metrics, so one query shows each agent's
/// guardrail check, release checks and whether a version was created (see README).
/// </summary>
public static class ReleaseGateMetrics
{
    /// <summary>The meter for this repo's own metrics: the release gate's, and the provisioning job's.</summary>
    public const string MeterName = "Dfe.Common.AI.Services";

    /// <summary>The tag naming the agent, as the library's metrics use it.</summary>
    public const string AgentNameTag = "gen_ai.agent.name";

    private const string OutcomeTag = "dfe.ai_agents.release_gate.outcome";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> GuardrailChecks = Meter.CreateCounter<long>(
        "dfe.common_ai_services.guardrail.checks", "{agent}", "Agents whose Foundry guardrail was checked, by outcome.");

    internal static void RecordGuardrailCheck(IEnumerable<AgentDefinition> agents, bool passed)
    {
        foreach (var agent in agents)
        {
            GuardrailChecks.Add(1, new(AgentNameTag, agent.Name), new(OutcomeTag, passed ? "passed" : "failed"));
        }
    }
}
