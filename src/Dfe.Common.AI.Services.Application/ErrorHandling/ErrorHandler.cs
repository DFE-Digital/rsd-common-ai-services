using Azure;
using Azure.Identity;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.ReleaseGate.Exceptions;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.AI.Agents.Evaluation.Exceptions;
using Dfe.Common.AI.Services.Application.Enums;

namespace Dfe.Common.AI.Services.Application.ErrorHandling;

/// <summary>
/// Handles exceptions thrown by the application and maps them to exit codes, logging the errors appropriately.
/// </summary>
public static class ErrorHandler
{
    public static ExitCode Handle(Exception exception, ILogger logger)
    {
        switch (exception)
        {
            // The release gate has already logged each reason as an error, in the form the README's queries read.
            case AgentReleaseBlockedException:
                return ExitCode.ReleaseBlocked;

            // Report the versions Foundry now has, then handle why provisioning stopped.
            case AgentProvisioningIncompleteException incomplete:
                var provisioned = string.Join(", ", incomplete.Provisioned.Select(a => $"{a.Name} {a.Version}"));
                var notProvisioned = string.Join(", ", incomplete.Failed);
                logger.ProvisioningIncomplete(provisioned, notProvisioned);
                return Handle(incomplete.InnerException!, logger);

            // A release-gate file, such as a baseline, that can't be read.
            case ReleaseGateException releaseGate:
                logger.InvalidConfiguration(releaseGate.Message);
                return ExitCode.InvalidConfiguration;

            case AgentConfigurationException configuration:
                logger.InvalidConfiguration(configuration.Message);
                return ExitCode.InvalidConfiguration;

            // A settings file, such as appsettings.json, that isn't valid JSON.
            case InvalidDataException { InnerException: FormatException } settingsFile:
                var unreadable = Messages.Errors.SettingsFileUnreadable(settingsFile);
                logger.InvalidConfiguration(unreadable);
                return ExitCode.InvalidConfiguration;

            case OperationCanceledException:
                logger.Cancelled();
                return ExitCode.Cancelled;
        }

        // Azure SDK failures usually arrive wrapped by the agents library, so look through the inner exceptions.
        if (Find<AuthenticationFailedException>(exception) is { } authentication)
        {
            logger.AuthenticationFailed(authentication.Message);
            return ExitCode.InvalidConfiguration;
        }

        if (Find<RequestFailedException>(exception) is { } request)
        {
            logger.AzureRequestFailed(request.Status, request.ErrorCode, request.Message);
            return ExitCode.AzureRequestFailed;
        }

        logger.UnexpectedError(exception);
        return ExitCode.Failed;
    }

    /// <summary>
    /// The one line the job writes to the console when it fails, so whoever runs it sees why without opening
    /// Application Insights: the job's own message, and the underlying cause where the job wrapped one.
    /// </summary>
    public static string Summarise(ExitCode exitCode, Exception exception)
    {
        // The job's own exceptions explain what happened; the base exception says why. Others are the cause already.
        var cause = exception.GetBaseException();
        var ownMessageAndCause = exception is AgentProvisioningIncompleteException or AgentConfigurationException;
        var reason = ownMessageAndCause && cause != exception ? $"{exception.Message} Cause: {cause.Message}" : cause.Message;

        return Messages.Errors.RunFailed(exitCode, reason);
    }

    private static T? Find<T>(Exception? exception) where T : Exception => exception switch
    {
        null => null,
        T match => match,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find<T>).FirstOrDefault(e => e is not null),
        _ => Find<T>(exception.InnerException),
    };
}
