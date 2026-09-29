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
    }

    /// <summary>
    /// Contains messages related to release gate checks performed on agents before they are provisioned.
    /// </summary>
    public static class ReleaseGate
    {
        public static string NotScored(string metric) => $"The judge gave no {metric} score; the warnings above say why.";

        public static string NoTestCases(string directory) => $"No test cases found in {directory}.";

        public static string TestCaseFailed(string caseName, IEnumerable<string> failures) =>
            $"Test case '{caseName}': {string.Join("; ", failures)}";

        public static string ScoreBelowMinimum(string metric, double score, double minimum) =>
            $"Average {metric} score {Score(score)} is below the minimum of {Score(minimum)}.";

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
        public const string AgentScores = "{AgentName} average judge scores: {Scores}";
        public const string AgentPassed = "{AgentName} passed its release checks. Report: {ReportPath}";
        public const string AgentFailed = "{AgentName} failed {Count} release checks. Report: {ReportPath}";
        public const string Provisioning = "All agents passed; creating or reusing their Foundry versions";
    }
}
