using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate;
using Dfe.Common.AI.Services.Application.QualityGate.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the common managed agents and the service that provisions them in Foundry.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddAgentProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        var qualityOptions = configuration.GetSection(AgentQualityOptions.SectionName).Get<AgentQualityOptions>()
            ?? new AgentQualityOptions();

        try
        {
            services.AddAiAgents(configuration, agents => ConfigureAgents(agents, qualityOptions));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // The agents library validates its settings here, before the host exists.
            throw new AgentConfigurationException(ex.Message, ex);
        }

        services.AddSingleton(qualityOptions);
        services.AddSingleton<IAgentReleaseGate, AgentReleaseGate>();
        services.AddSingleton<IAgentProvisioningService, AgentProvisioningService>();

        return services;
    }

    private static void ConfigureAgents(AiAgentsBuilder agents, AgentQualityOptions quality)
    {
        agents.AddAgents(CommonAgents.All);

        if (quality.HasJudge && quality.JudgeModel is { } judgeModel)
        {
            // Scores test answers for groundedness in the evidence and relevance to the prompt (JudgeMetrics). A
            // sample rate of 0 scores only release-gate runs; live sampling belongs in the apps that run the agents.
            agents.AddQualityEvaluation(judgeModel, sampleRate: 0);
        }
    }
}
