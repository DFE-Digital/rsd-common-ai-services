using GovUK.Dfe.AI.Agents.Guardrails.Enums;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.ReleaseGate.Constants;

/// <summary>
/// The release gate's log events, generated from the <see cref="Messages.Log"/> templates, so a message is only formatted
/// when its level is on. Event IDs are unchanged from when the gate was part of the provisioning job.
/// </summary>
internal static partial class LogEvents
{
    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = Messages.Log.CheckingGuardrail)]
    public static partial void CheckingGuardrail(this ILogger logger, string? guardrail, GuardrailTargets targets);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = Messages.Log.GuardrailInPlace)]
    public static partial void GuardrailInPlace(this ILogger logger, string guardrail);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = Messages.Log.ReleaseChecksStarting)]
    public static partial void ReleaseChecksStarting(this ILogger logger, int count, string agents);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = Messages.Log.ReleaseChecksFailed)]
    public static partial void ReleaseChecksFailed(this ILogger logger, int count, string agents);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = Messages.Log.ReleaseSectionsResult)]
    public static partial void ReleaseSectionsPassed(this ILogger logger, string agentName, string guardrail, string evaluation);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = Messages.Log.ReleaseSectionsResult)]
    public static partial void ReleaseSectionsFailed(this ILogger logger, string agentName, string guardrail, string evaluation);
}
