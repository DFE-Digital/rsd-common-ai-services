using System.Diagnostics;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.QualityGate.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.Agents;

public sealed class AgentProvisioningService(
    IAgentService agents,
    IAgentReleaseGate releaseGate,
    ILogger<AgentProvisioningService> logger) : IAgentProvisioningService
{
    public async Task<IReadOnlyList<AgentReference>> ProvisionAsync(CancellationToken cancellationToken = default)
    {
        var definitions = CommonAgents.All;
        logger.LogInformation(Messages.Log.ReleaseChecksStarting, definitions.Length, string.Join(", ", definitions.Select(a => a.Name)));

        var gate = await releaseGate.EvaluateAsync(definitions, cancellationToken);
        var failed = gate.Where(r => !r.Passed).ToList();
        if (failed.Count != 0)
        {
            logger.LogError(Messages.Log.ReleaseChecksFailed, failed.Count, string.Join(", ", failed.Select(r => r.AgentName)));
            throw new AgentReleaseBlockedException(failed);
        }

        // The library provisions the agents it's given one after another, so each agent gets its own call and they
        // run side by side. The library logs each agent's name and version as it's provisioned.
        logger.LogInformation(Messages.Log.Provisioning);
        var started = Stopwatch.GetTimestamp();
        var provisioned = await Task.WhenAll(definitions.Select(definition =>
            agents.ProvisionAsync([definition], cancellationToken)));

        logger.LogInformation(Messages.Log.ProvisioningFinished, definitions.Length, Stopwatch.GetElapsedTime(started).TotalSeconds);
        return [.. provisioned.SelectMany(references => references)];
    }
}
