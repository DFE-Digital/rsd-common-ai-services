using System.Diagnostics;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.ReleaseGate.Exceptions;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
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
        var agentNames = string.Join(", ", definitions.Select(a => a.Name));
        logger.Starting(definitions.Length, agentNames);

        // One release: the guardrail check, the release checks and the versions provisioned are all recorded under its
        // id, by the agents library, so one query shows each agent's whole release (see README).
        using var run = releaseGate.BeginRun();

        // The library's standard release check: the guardrail, then every agent's guardrail and evaluation test cases,
        // each agent's result logged. Nothing is provisioned unless every agent passed.
        var results = await releaseGate.CheckReleaseAsync(definitions, cancellationToken);
        var failed = results.Where(static result => !result.Passed).ToList();
        if (failed.Count != 0)
        {
            throw new AgentReleaseBlockedException(failed);
        }

        // The library provisions the agents it's given one after another, so each agent gets its own call and they
        // run side by side. The library logs each agent's name and version as it's provisioned.
        logger.Provisioning();
        var started = Stopwatch.GetTimestamp();
        var provisioning = definitions.Select(definition => agents.ProvisionAsync([definition], cancellationToken)).ToArray();
        try
        {
            await Task.WhenAll(provisioning);
        }
        catch (Exception ex)
        {
            ThrowIfPartlyProvisioned(definitions, provisioning, ex);
            throw;
        }

        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        logger.ProvisioningFinished(definitions.Length, seconds);
        var provisioned = provisioning.SelectMany(task => task.Result).ToList();
        foreach (var agent in provisioned)
        {
            logger.AgentVersion(agent.Name, agent.Version);
        }

        return provisioned;
    }

    /// <summary>
    /// Agents are provisioned side by side, so one can fail after others have new versions. Those versions must be
    /// reported, as no consuming app knows about them. If none were provisioned, Foundry is unchanged and the original
    /// failure is enough.
    /// </summary>
    private static void ThrowIfPartlyProvisioned(AgentDefinition[] definitions,
        Task<IReadOnlyList<AgentReference>>[] provisioning, Exception failure)
    {
        var provisioned = provisioning.Where(task => task.IsCompletedSuccessfully).SelectMany(task => task.Result).ToList();
        if (provisioned.Count == 0)
        {
            return;
        }

        var failed = definitions.Where((_, index) => !provisioning[index].IsCompletedSuccessfully).Select(d => d.Name).ToList();
        throw new AgentProvisioningIncompleteException(provisioned, failed, failure);
    }
}
