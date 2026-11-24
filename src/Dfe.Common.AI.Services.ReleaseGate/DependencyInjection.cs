using Dfe.Common.AI.Services.ReleaseGate.Models;
using GovUK.Dfe.AI.Agents.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.ReleaseGate;

public static class DependencyInjection
{
    /// <summary>
    /// Adds the release checks to the agent builder: the guardrail, the release gate with the agents' fixed refusals,
    /// and the library's Responsible AI quality gate, which decides the release from them by the policy under
    /// <c>AiAgents:Evaluation:QualityGate</c>. Answers are scored by the judge only if <paramref name="hasJudge"/>.
    /// </summary>
    /// <param name="agents">The agent builder to add checks to.</param>
    /// <param name="hasJudge">Whether a judge model scores answers; without one, the judge's checks don't apply.</param>
    /// <returns>The updated agent builder.</returns>
    public static AgentsBuilder AddReleaseChecks(this AgentsBuilder agents, bool hasJudge)
    {
        ArgumentNullException.ThrowIfNull(agents);

        return agents.AddGuardrails()
            .AddResponsibleAiQualityGate(gate =>
            {
                if (!hasJudge)
                {
                    gate.WithoutJudge();
                }
            })
            .AddReleaseGate(gate => gate.RefusalPhrases.AddRange(FixedResponses.Refusals));
    }
}
