using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.Constants;

/// <summary>
/// The job's log events, generated from the <see cref="Messages.Log"/> templates, so a message is only formatted when
/// its level is on. Progress is Information; raising the level turns it off (see README).
/// </summary>
internal static partial class LogEvents
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = Messages.Log.Starting)]
    public static partial void Starting(this ILogger logger, int count, string agents);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = Messages.Log.Provisioning)]
    public static partial void Provisioning(this ILogger logger);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = Messages.Log.ProvisioningFinished)]
    public static partial void ProvisioningFinished(this ILogger logger, int count, double seconds);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = Messages.Log.AgentVersion)]
    public static partial void AgentVersion(this ILogger logger, string agentName, string? version);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = Messages.Log.ProvisioningIncomplete)]
    public static partial void ProvisioningIncomplete(this ILogger logger, string provisioned, string failed);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = Messages.Log.InvalidConfiguration)]
    public static partial void InvalidConfiguration(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Warning, Message = Messages.Log.Cancelled)]
    public static partial void Cancelled(this ILogger logger);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Error, Message = Messages.Log.AuthenticationFailed)]
    public static partial void AuthenticationFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Error, Message = Messages.Log.AzureRequestFailed)]
    public static partial void AzureRequestFailed(this ILogger logger, int status, string? errorCode, string reason);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Critical, Message = Messages.Log.UnexpectedError)]
    public static partial void UnexpectedError(this ILogger logger, Exception exception);
}
