using Dfe.Common.AI.Services.ReleaseGate.Models;
using Dfe.Common.AI.Services.ReleaseGate.Services;
using Dfe.Common.AI.Services.ReleaseGate.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.ReleaseGate;

public static class DependencyInjection
{
    /// <summary>
    /// Adds the release checks to the agent builder, including guardrails and refusal phrases. If <paramref name="hasJudge"/> is true, it also adds quality evaluation.
    /// </summary>
    /// <param name="agents">The agent builder to add checks to.</param>
    /// <param name="hasJudge">Whether to include quality evaluation.</param>
    /// <returns>The updated agent builder.</returns>
    public static AgentsBuilder AddReleaseChecks(this AgentsBuilder agents, bool hasJudge)
    {
        ArgumentNullException.ThrowIfNull(agents);
         
        agents.AddGuardrails()
            .AddReleaseGate(gate => gate.RefusalPhrases.AddRange(FixedResponses.Refusals));

        if (hasJudge)
        {
            agents.AddQualityEvaluation();
        }

        return agents;
    }

    /// <summary>Registers <see cref="IReleaseGateService"/>, which runs the checks <see cref="AddReleaseChecks"/> adds.</summary>
    public static IServiceCollection AddReleaseGateService(this IServiceCollection services)
    {
        services.AddSingleton<IReleaseGateService, ReleaseGateService>();
        return services;
    }
}
