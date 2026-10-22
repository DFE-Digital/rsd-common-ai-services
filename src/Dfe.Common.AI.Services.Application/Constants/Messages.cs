using GovUK.Dfe.AI.Agents.ValueObjects;
using Dfe.Common.AI.Services.Application.Enums;

namespace Dfe.Common.AI.Services.Application.Constants;

/// <summary>
/// Contains messages used in the application, including error messages, log templates, and feedback for agents.
/// </summary>
public static class Messages
{
    /// <summary>
    /// Contains messages related to answer checks performed on agent outputs.
    /// </summary>
    public static class AnswerChecks
    {
        public const string EmptyAnswer = "The answer is empty. Answer the question, or say plainly that the evidence doesn't answer it.";

        public const string NoSources =
            "End the answer with a line \"Sources:\" and then one line for each record you cited, written only as " +
            "- [Evidence n].";

        public const string LinksInSources =
            "Write each line of the Sources section only as - [Evidence n], with no links, URLs or names: the record's " +
            "name and link are added for you.";

        public static string SourcesMissing(IEnumerable<int> evidenceNumbers) =>
            "Add these cited records to the Sources section: " +
            string.Join(", ", evidenceNumbers.Select(n => $"[Evidence {n}]")) + ".";

        /// <summary>
        /// Sent back when an answer doesn't address an outcome the calling app expected. It asks the agent to check the
        /// evidence, never to agree: a reason that said only "mention X" would push it to state X as fact.
        /// </summary>
        public static string ExpectedOutcomeNotAddressed(IEnumerable<string> outcomes) =>
            "The calling application expected the answer to address: " + string.Join(", ", outcomes.Select(o => $"\"{o}\"")) +
            ". For each, if the evidence supports it, state it with its citation; if not, say plainly that the evidence " +
            "doesn't show it and what the evidence shows instead. Never state it just because it was expected.";
    }

    /// <summary>
    /// Contains error messages used in the application.
    /// </summary>
    public static class Errors
    {
        /// <summary>Which settings file couldn't be read, and where the parser stopped.</summary>
        public static string SettingsFileUnreadable(Exception exception) =>
            $"{exception.Message} {exception.GetBaseException().Message}";

        public const string ApplicationInsightsMissing =
            "APPLICATIONINSIGHTS_CONNECTION_STRING isn't set, so token usage, cost and release-gate results can't be sent to " +
            "Application Insights. Set it to the Application Insights connection string, or, for local development " +
            "only, set AiAgents:RequireTokenUsageTelemetry to false.";

        public const string NoExpectedOutcome = "Give at least one expected outcome, and no empty ones.";

        public static string ProvisioningIncomplete(IEnumerable<AgentReference> provisioned, IEnumerable<string> failed) =>
            $"Provisioning stopped part way. Foundry has new versions of {string.Join(", ", provisioned.Select(a => $"{a.Name} {a.Version}"))}, " +
            $"which no app has been told about; {string.Join(", ", failed)} weren't provisioned. Fix the cause and run again.";

        /// <summary>The one line the job writes to the console when it fails, even if logging isn't set up yet.</summary>
        public static string RunFailed(ExitCode exitCode, string reason) =>
            $"Failed with exit code {(int)exitCode} ({exitCode}): {reason}";
    }

    /// <summary>
    /// Contains log message templates used in the application.
    /// </summary>
    public static class Log
    {
        public const string ReleaseBlocked = "Agent release blocked by gate: {Problems}";
        public const string ProvisioningIncomplete = "Provisioning stopped part way; new versions no app knows about: {Provisioned}; not provisioned: {Failed}";
        public const string InvalidConfiguration = "The settings are invalid: {Reason}";
        public const string AuthenticationFailed = "Couldn't sign in to Azure; check AiAgents:Authentication: {Reason}";
        public const string AzureRequestFailed = "Azure refused a request ({Status} {ErrorCode}); check the service principal's roles: {Reason}";
        public const string Cancelled = "Provisioning was cancelled or timed out before it finished";
        public const string UnexpectedError = "Provisioning failed unexpectedly";

        // Progress: Information, or Warning for a failure. Turned off by raising the log level (see README).
        public const string Starting = "Provisioning {Count} agents: {Agents}";
        public const string Provisioning = "All agents passed; creating or reusing their Foundry versions";
        public const string ProvisioningFinished = "Provisioned {Count} agents in {Seconds:0.0} seconds";
        public const string AgentVersion = "{AgentName} is at version {Version}; apps should pin this version";
        public const string GuardrailNotInPlace = "The Foundry guardrail isn't in place: {Reason}";
    }
}
