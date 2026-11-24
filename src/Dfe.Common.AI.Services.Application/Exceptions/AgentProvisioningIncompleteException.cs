using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Exceptions;

/// <summary>
/// Thrown when some agents were provisioned and others weren't, so Foundry has new versions that no consuming app has
/// been told about.
/// </summary>
/// <param name="provisioned">The agents provisioned, with their versions.</param>
/// <param name="failed">The names of the agents that weren't.</param>
/// <param name="innerException">Why the first of them failed.</param>
public sealed class AgentProvisioningIncompleteException(
    IReadOnlyList<AgentReference> provisioned, IReadOnlyList<string> failed, Exception innerException)
    : InvalidOperationException(Messages.Errors.ProvisioningIncomplete(provisioned, failed), innerException)
{
    public IReadOnlyList<AgentReference> Provisioned { get; } = provisioned;

    public IReadOnlyList<string> Failed { get; } = failed;
}
