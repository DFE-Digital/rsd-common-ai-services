namespace Dfe.Common.AI.Services.Application.Options;

/// <summary>
/// Options for the agent quality evaluation service, which checks each agent's answers against its test cases and judge scores.
/// </summary>
public sealed class AgentQualityOptions
{
    public const string SectionName = "AgentQuality";

    /// <summary>
    /// The Foundry model deployment that scores answers for groundedness and relevance (1 to 5). When empty, answers
    /// aren't scored and only each test case's <c>mustMention</c> and <c>mustNotMention</c> are checked.
    /// </summary>
    public string? JudgeModel { get; set; }

    /// <summary>Holds one folder of test cases per agent, named after the agent. Relative to the app's folder.</summary>
    public string TestCasesDirectory { get; set; } = "TestCases";

    /// <summary>Where each run writes its reports. Relative to the working directory.</summary>
    public string ReportsDirectory { get; set; } = "evaluation-reports";

    /// <summary>The lowest average judge score, per metric, that an agent may release with.</summary>
    public double MinimumScore { get; set; } = 3.5;

    /// <summary>Whether answers are scored by a judge model.</summary>
    public bool HasJudge => !string.IsNullOrWhiteSpace(JudgeModel);
}
