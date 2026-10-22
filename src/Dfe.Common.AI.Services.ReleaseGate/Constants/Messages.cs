using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;

namespace Dfe.Common.AI.Services.ReleaseGate.Constants;

/// <summary>The release gate's messages: why agents can't be released, and its log templates.</summary>
public static class Messages
{
    public static class Errors
    {
        public const string NotTested = "The release checks returned no result for it, so it wasn't tested.";

        public static string GuardrailNotInPlace(string guardrail, IEnumerable<string> problems) =>
            $"The Foundry guardrail '{guardrail}' isn't in place, so no agents were tested or provisioned. Create or fix it in the Foundry portal to match AiAgents:Guardrails: " +
            string.Join("; ", problems) + ".";

        /// <summary>One line per problem, guardrail first, each marked with its section: <c>- agent [guardrail]: …</c>.</summary>
        public static string ReleaseBlocked(IEnumerable<AgentGateResult> failed) =>
            "No agents were provisioned because these failed their release checks:" + Environment.NewLine +
            string.Join(Environment.NewLine, failed.SelectMany(r =>
                new[] { ReleaseSections.Guardrail, ReleaseSections.Evaluation }.SelectMany(section =>
                    ReleaseSections.Problems(r, section).Select(p => $"- {r.AgentName} [{section}]: {p}"))));

        /// <summary>A section's result: "passed", or "failed" with how many problems.</summary>
        public static string SectionResult(int problems) => problems == 0 ? "passed" : $"failed ({problems} problems)";
    }

    /// <summary>Progress: Information, or Warning for a failure. Turned off by raising the log level (see README).</summary>
    public static class Log
    {
        public const string CheckingGuardrail = "Checking Foundry guardrail {Guardrail} for {Targets}";
        public const string GuardrailInPlace = "Foundry guardrail {Guardrail} is in place";
        public const string ReleaseChecksStarting = "Running release checks for {Count} agents: {Agents}";
        public const string ReleaseSectionsResult = "{AgentName}: guardrail {Guardrail}, evaluation {Evaluation}";
        public const string ReleaseChecksFailed = "Agent release checks failed for {Count} agents: {Agents}";
    }
}
