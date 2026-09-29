namespace Dfe.Common.AI.Services.Application.ErrorHandling;

/// <summary>
/// Exit codes for the application. These are returned to the operating system when the application exits, and can be used in scripts or other automation to determine the outcome of the application.
/// </summary>
public enum ExitCode
{
    Success = 0,

    /// <summary>Something unexpected failed. The log has the stack trace.</summary>
    Failed = 1,

    /// <summary>One or more agents failed their release checks. Nothing was provisioned.</summary>
    ReleaseBlocked = 2,

    /// <summary>Settings are missing or wrong, including credentials Azure rejected.</summary>
    InvalidConfiguration = 3,

    /// <summary>Azure refused a request, for example because a role is missing.</summary>
    AzureRequestFailed = 4,

    /// <summary>The job was stopped or timed out before it finished.</summary>
    Cancelled = 5,
}
