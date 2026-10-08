using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Extensions;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate;
using Dfe.Common.AI.Services.Application.QualityGate.Interfaces;
using GovUK.Dfe.AI.Agents.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.Application;

public static class DependencyInjection
{
    private const string JudgeModelKey = "AiAgents:Evaluation:JudgeModel";
    private const string LegacyJudgeModelKey = "AgentQuality:JudgeModel"; 

    /// <summary>
    /// Registers the common managed agents and the service that provisions them in Foundry.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddAgentProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration[LegacyJudgeModelKey]))
        {
            throw new AgentConfigurationException(Messages.Errors.JudgeModelMoved);
        }

        var qualityOptions = configuration.GetSection(AgentQualityOptions.SectionName).Get<AgentQualityOptions>()
            ?? new AgentQualityOptions();
        qualityOptions.JudgeModel = configuration[JudgeModelKey];

        try
        {
            services.AddAgents(configuration, agents => ConfigureAgents(agents, qualityOptions));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // The agents library validates its settings here, before the host exists.
            throw new AgentConfigurationException(ex.Message, ex);
        }

        services.AddApplicationInsights(configuration);
        services.AddSingleton(qualityOptions);
        services.AddSingleton<IAgentReleaseGate, AgentReleaseGate>();
        services.AddSingleton<IAgentProvisioningService, AgentProvisioningService>();

        return services;
    }

    private static void ConfigureAgents(AgentsBuilder agents, AgentQualityOptions quality)
    {
        agents.AddAgents(CommonAgents.All).AddGuardrails();

        if (quality.HasJudge)
        {
            // Scores test answers for groundedness in the evidence and relevance to the prompt (JudgeMetrics). Its
            // settings are under AiAgents:Evaluation; a SampleRate of 0 scores only release-gate answers, as live
            // sampling belongs in the apps that run the agents.
            agents.AddQualityEvaluation();
        }
    } 
}
