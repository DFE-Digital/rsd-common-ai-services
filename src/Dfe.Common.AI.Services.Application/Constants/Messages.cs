using GovUK.Dfe.AI.Agents.ValueObjects;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;

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
            "APPLICATIONINSIGHTS_CONNECTION_STRING isn't set, so token usage, logs and release-gate results can't be sent to " +
            "Application Insights. Set it to the Application Insights connection string, or, for local development " +
            "only, set AiAgents:RequireTokenUsageTelemetry to false.";

        public static string GuardrailNotInPlace(string guardrail, IEnumerable<string> problems) =>
            $"The Foundry guardrail '{guardrail}' isn't in place, so no agents were tested or provisioned. Create or fix it in the Foundry portal to match AiAgents:Guardrails: " +
            string.Join("; ", problems) + ".";

        public static string ProvisioningIncomplete(IEnumerable<AgentReference> provisioned, IEnumerable<string> failed) =>
            $"Provisioning stopped part way. Foundry has new versions of {string.Join(", ", provisioned.Select(a => $"{a.Name} {a.Version}"))}, " +
            $"which no app has been told about; {string.Join(", ", failed)} weren't provisioned. Fix the cause and run again.";

        /// <summary>The one line the job writes to the console when it fails, as its logs are in Application Insights.</summary>
        public static string RunFailed(ExitCode exitCode, string reason) =>
            $"Failed with exit code {(int)exitCode} ({exitCode}): {reason}";

        public static string ReleaseBlocked(IEnumerable<AgentGateResult> failed) =>
            "No agents were provisioned because these failed their release checks:" + Environment.NewLine +
            string.Join(Environment.NewLine, failed.SelectMany(r => r.Problems.Select(p => $"- {r.AgentName}: {p}")));
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
        public const string ReleaseChecksFailed = "Agent release checks failed for {Count} agents: {Agents}";

        // Progress: Information, or Warning for a failure. Turned off by raising the log level (see README).
        public const string ReleaseChecksStarting = "Running release checks for {Count} agents: {Agents}";
        public const string Provisioning = "All agents passed; creating or reusing their Foundry versions";
        public const string ProvisioningFinished = "Provisioned {Count} agents in {Seconds:0.0} seconds";
        public const string CheckingGuardrail = "Checking Foundry guardrail {Guardrail} for {Targets}";
        public const string GuardrailInPlace = "Foundry guardrail {Guardrail} is in place";
        public const string GuardrailNotInPlace = "The Foundry guardrail isn't in place: {Reason}";
    }
}
