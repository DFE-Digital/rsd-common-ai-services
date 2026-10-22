using System.Diagnostics.Metrics;
using Dfe.Common.AI.Services.ReleaseGate.Metrics;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// The agent versions the job created, on the same meter and tags as the release gate's metrics
/// (<see cref="ReleaseGateMetrics"/>), so one query shows each agent's checks and whether a version was created (see README).
/// </summary>
internal static class ReleaseMetrics
{
    private static readonly Meter Meter = new(ReleaseGateMetrics.MeterName);

    private static readonly Counter<long> Provisioned = Meter.CreateCounter<long>(
        "dfe.common_ai_services.agents.provisioned", "{agent}", "Agent versions created or reused in Foundry, by agent and version.");

    public static void RecordProvisioned(IEnumerable<AgentReference> agents)
    {
        foreach (var agent in agents)
        {
            Provisioned.Add(1, new(ReleaseGateMetrics.AgentNameTag, agent.Name), new("gen_ai.agent.version", agent.Version));
        }
    }
}
