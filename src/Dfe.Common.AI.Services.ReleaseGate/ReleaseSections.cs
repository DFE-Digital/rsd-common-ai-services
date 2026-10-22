using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;

namespace Dfe.Common.AI.Services.ReleaseGate;

/// <summary>
/// sections of the library's release gate: guardrail and evaluation, and the test case names that belong to each section.
/// </summary>
public static class ReleaseSections
{
    public const string Guardrail = "guardrail";
    public const string Evaluation = "evaluation";

    /// <summary>
    /// Prefix of a guardrail test case's name, e.g. <c>guardrail/guardrail-1</c>.
    /// </summary>
    public const string GuardrailCasePrefix = Guardrail + "/";

    /// <summary>The start of an evaluation test case's name, e.g. <c>evaluation/ks2-trend</c>.</summary>
    public const string EvaluationCasePrefix = Evaluation + "/";

    /// <summary>
    /// Provides the section of the release gate that a problem belongs to, based on its name or content.
    /// </summary>
    /// <param name="problem">The problem to categorise.</param>
    /// <returns>The section to which the problem belongs.</returns>
    public static string Of(string problem) =>
        problem.StartsWith($"Case {GuardrailCasePrefix}", StringComparison.Ordinal)
        || problem.Contains("guardrail", StringComparison.OrdinalIgnoreCase)
        || problem.Contains("Prompt Shields", StringComparison.Ordinal)
        || problem.Contains("matched the blocklist", StringComparison.Ordinal)
            ? Guardrail
            : Evaluation;

    /// <summary>An agent's problems in one section.</summary>
    public static IReadOnlyList<string> Problems(AgentGateResult result, string section) =>
        [.. result.Problems.Where(problem => Of(problem) == section)];
}
