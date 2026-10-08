using System.Globalization;
using Dfe.Common.AI.Services.Application.ValueObjects;

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
            "End the answer with a line \"Sources:\" and then one line for each record you cited: " +
            "[Evidence n] the record's name and identifier (the index named in its evidence heading).";

        public static string SourcesMissing(IEnumerable<int> evidenceNumbers) =>
            "Add these cited records to the Sources section: " +
            string.Join(", ", evidenceNumbers.Select(n => $"[Evidence {n}]")) + ".";
    }

    /// <summary>
    /// Contains messages related to release gate checks performed on agents before they are provisioned.
    /// </summary>
    public static class ReleaseGate
    {
        public static string NoTestCases(string directory) => $"No test cases found in {directory}.";

        /// <summary>The recorded answer for a test case the Foundry guardrail blocked, where the case allows it.</summary>
        public const string BlockedByGuardrail = "Blocked by the Foundry guardrail, which this test case allows.";

        /// <summary>Average scores for the log, e.g. "Groundedness 4.50, Relevance 4.25".</summary>
        public static string Scores(IReadOnlyDictionary<string, double> scores) =>
            scores.Count == 0 ? "none" : string.Join(", ", scores.OrderBy(s => s.Key).Select(s => $"{s.Key} {Score(s.Value)}"));

        private static string Score(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Contains error messages used in the application.
    /// </summary>
    public static class Errors
    {
        /// <summary>Which settings file couldn't be read, and where the parser stopped.</summary>
        public static string SettingsFileUnreadable(Exception exception) =>
            $"{exception.Message} {exception.GetBaseException().Message}";

        public const string JudgeModelMoved =
            "AgentQuality:JudgeModel has moved to AiAgents:Evaluation:JudgeModel. Move the setting there, and keep " +
            "AiAgents:Evaluation:SampleRate at 0 so only release-gate answers are scored.";

        public const string ApplicationInsightsMissing =
            "APPLICATIONINSIGHTS_CONNECTION_STRING isn't set, so token usage, logs and release-gate results can't be sent to " +
            "Application Insights. Set it to the Application Insights connection string, or, for local development " +
            "only, set AiAgents:RequireTokenUsageTelemetry to false.";

        public static string BaselineUnreadable(string path, string reason) =>
            $"The baseline {path} isn't a readable evaluation report: {reason}";

        public static string GuardrailsNotApplied(string guardrail, IEnumerable<string> problems) =>
            $"The Foundry guardrail '{guardrail}' isn't in place, so no agents were tested or provisioned: " +
            string.Join("; ", problems) + ".";

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
        public const string LeftoverTestAgentsNotDeleted = "Couldn't check Foundry for leftover test agents; the next run will try again";
        public const string InvalidConfiguration = "The settings are invalid: {Reason}";
        public const string AuthenticationFailed = "Couldn't sign in to Azure; check AiAgents:Authentication: {Reason}";
        public const string AzureRequestFailed = "Azure refused a request ({Status} {ErrorCode}); check the service principal's roles: {Reason}";
        public const string Cancelled = "Provisioning was cancelled or timed out before it finished";
        public const string UnexpectedError = "Provisioning failed unexpectedly";
        public const string ReleaseChecksFailed = "Agent release checks failed for {Count} agents: {Agents}";

        // Progress: Information, or Warning for a failure. Turned off by raising the log level (see README).
        public const string ReleaseChecksStarting = "Running release checks for {Count} agents: {Agents}";
        public const string JudgeOn = "Answers are scored by judge model {JudgeModel}; each metric must average at least {MinimumScore}";
        public const string JudgeOff = "No judge model is set, so answers are checked against their test cases only";
        public const string TestingAgent = "Testing {AgentName} with {Count} test cases on a temporary copy";
        public const string NoTestCases = "{AgentName} has no test cases in {Directory}";
        public const string TestCasePassed = "{AgentName} / {CaseName}: passed";
        public const string TestCaseFailed = "{AgentName} / {CaseName}: failed: {Failures}";
        public const string AgentScores = "{AgentName} average judge scores: {Scores} ({Refusals} cases expecting a refusal not scored)";
        public const string AgentPassed = "{AgentName} passed its release checks. Report: {ReportPath}";
        public const string AgentFailed = "{AgentName} failed {Count} release checks. Report: {ReportPath}";
        public const string Provisioning = "All agents passed; creating or reusing their Foundry versions";
        public const string RunningTestCases = "Running {Count} test cases, up to {MaxParallel} at a time";
        public const string ReleaseChecksFinished = "Release checks finished in {Seconds:0.0} seconds";
        public const string ProvisioningFinished = "Provisioned {Count} agents in {Seconds:0.0} seconds";
        public const string ApplyingGuardrails = "Applying Foundry guardrail {Guardrail} to model deployments: {Deployments}";
        public const string GuardrailsApplied = "Foundry guardrail {Guardrail} is in place";
        public const string GuardrailsNotApplied = "The Foundry guardrail isn't in place: {Reason}";
        public const string ComparingWithBaseline = "{AgentName} scores are compared with its baseline {Path}";
        public const string NoBaseline = "{AgentName} has no baseline at {Path}, so it's held to the minimum score only";
        public const string TestCaseBlockedByGuardrail = "{AgentName} / {CaseName}: blocked by the Foundry guardrail, which this case allows";
    }
}
