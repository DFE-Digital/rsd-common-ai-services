using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Extensions;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.AI.Agents.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.Application;

public static class DependencyInjection
{
    private const string JudgeModelKey = "AiAgents:Evaluation:JudgeModel";

    /// <summary>
    /// Registers the common managed agents and the service that provisions them in Foundry.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddAgentProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        var hasJudge = !string.IsNullOrWhiteSpace(configuration[JudgeModelKey]);

        try
        {
            services.AddAgents(configuration, agents => ConfigureAgents(agents, hasJudge));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // The agents library validates its settings here, before the host exists.
            throw new AgentConfigurationException(ex.Message, ex);
        }

        services.AddObservability(configuration);
        services.AddSingleton<IAgentProvisioningService, AgentProvisioningService>();

        return services;
    }

    private static void ConfigureAgents(AgentsBuilder agents, bool hasJudge)
    {
        // The release gate's settings are under AiAgents:Evaluation:ReleaseGate. Cases expecting one of the prompts'
        // fixed refusals are checked by that sentence and left out of the scores.
        agents.AddAgents(CommonAgents.All)
            .AddGuardrails()
            .AddReleaseGate(gate => gate.RefusalPhrases.AddRange(FixedResponses.Refusals));

        if (hasJudge)
        {
            // Scores test answers for groundedness in the evidence and relevance to the prompt. Its settings are under
            // AiAgents:Evaluation; a SampleRate of 0 scores only release-gate answers, as live sampling belongs in the
            // apps that run the agents.
            agents.AddQualityEvaluation();
        }
    }
}
