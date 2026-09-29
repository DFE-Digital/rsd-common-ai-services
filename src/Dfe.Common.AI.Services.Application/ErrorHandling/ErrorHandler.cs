using Azure;
using Azure.Identity;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Exceptions;
using Microsoft.Extensions.Logging;

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
            case AgentReleaseBlockedException blocked:
                logger.LogError(Messages.Log.ReleaseBlocked, blocked.Message);
                return ExitCode.ReleaseBlocked;

            case AgentConfigurationException configuration:
                logger.LogError(Messages.Log.InvalidConfiguration, configuration.Message);
                return ExitCode.InvalidConfiguration;

            // A settings file, such as appsettings.json, that isn't valid JSON.
            case InvalidDataException { InnerException: FormatException } settingsFile:
                logger.LogError(Messages.Log.InvalidConfiguration, Messages.Errors.SettingsFileUnreadable(settingsFile));
                return ExitCode.InvalidConfiguration;

            case OperationCanceledException:
                logger.LogWarning(Messages.Log.Cancelled);
                return ExitCode.Cancelled;
        }

        // Azure SDK failures usually arrive wrapped by the agents library, so look through the inner exceptions.
        if (Find<AuthenticationFailedException>(exception) is { } authentication)
        {
            logger.LogError(Messages.Log.AuthenticationFailed, authentication.Message);
            return ExitCode.InvalidConfiguration;
        }

        if (Find<RequestFailedException>(exception) is { } request)
        {
            logger.LogError(Messages.Log.AzureRequestFailed, request.Status, request.ErrorCode, request.Message);
            return ExitCode.AzureRequestFailed;
        }

        logger.LogCritical(exception, Messages.Log.UnexpectedError);
        return ExitCode.Failed;
    }

    private static T? Find<T>(Exception? exception) where T : Exception => exception switch
    {
        null => null,
        T match => match,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find<T>).FirstOrDefault(e => e is not null),
        _ => Find<T>(exception.InnerException),
    };
}
