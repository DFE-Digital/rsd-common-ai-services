using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// The release gate's two sections, each a folder of test cases, so a failure says what to fix. <b>Guardrail</b>: cases in
/// <c>guardrail/</c>, which must be blocked, and any evaluation case a guardrail blocked by mistake; fix the guardrail
/// settings. <b>Evaluation</b>: cases in <c>evaluation/</c>, and problems such as low judge scores, group gaps and baseline
/// drops; fix the prompt or the test case.
/// </summary>
public static class ReleaseSections
{
    public const string Guardrail = "guardrail";
    public const string Evaluation = "evaluation";

    /// <summary>
    /// The start of a guardrail test case's name: the library names each case after its folder and file, e.g.
    /// <c>guardrail/jailbreak-prompt</c>.
    /// </summary>
    public const string GuardrailCasePrefix = Guardrail + "/";

    /// <summary>The start of an evaluation test case's name, e.g. <c>evaluation/ks2-trend</c>.</summary>
    public const string EvaluationCasePrefix = Evaluation + "/";

    /// <summary>
    /// The section a gate problem belongs to: a guardrail case's, or a block of an evaluation case, which the library
    /// reports as a guardrail, Prompt Shields or blocklist block.
    /// </summary>
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
