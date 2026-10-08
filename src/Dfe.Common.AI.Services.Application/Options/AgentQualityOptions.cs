namespace Dfe.Common.AI.Services.Application.Options;

/// <summary>
/// Options for the agent quality evaluation service, which checks each agent's answers against its test cases and judge scores.
/// </summary>
public sealed class AgentQualityOptions
{
    public const string SectionName = "AgentQuality";

    /// <summary>
    /// The model that scores answers for groundedness and relevance (1 to 5), taken from
    /// <c>AiAgents:Evaluation:JudgeModel</c>, where the Evaluation package reads it. When empty, answers aren't scored
    /// and only each test case's <c>mustMention</c> and <c>mustNotMention</c> are checked.
    /// </summary>
    public string? JudgeModel { get; internal set; }

    /// <summary>Holds one folder of test cases per agent, named after the agent. Relative to the app's folder.</summary>
    public string TestCasesDirectory { get; set; } = "TestCases";

    /// <summary>Where each run writes its reports. Relative to the working directory.</summary>
    public string ReportsDirectory { get; set; } = "evaluation-reports";

    /// <summary>The lowest average judge score, per metric, that an agent may release with.</summary>
    public double MinimumScore { get; set; } = 3.5;

    /// <summary>
    /// How many test cases run at once, across all agents. Each uses a Foundry agent run and, with a judge, two judge
    /// calls, so keep it within your Foundry quota, and no higher than <c>AiAgents:MaxConcurrency</c> if that's set.
    /// </summary>
    public int MaxParallelTestRuns { get; set; } = 4;

    /// <summary>
    /// How many times each test case runs. Scores are averaged over the repeats, so one unlucky judge score doesn't
    /// decide a release, and the case's facts must be right every time. Each repeat costs another run and judge call.
    /// </summary>
    public int Repeats { get; set; } = 1;

    /// <summary>
    /// How far, per metric, an agent's average score may fall below its baseline before the release is blocked. Judges
    /// score the same answer slightly differently from run to run, so this stops noise blocking a release.
    /// </summary>
    public double Tolerance { get; set; } = 0.2;

    /// <summary>
    /// Holds each agent's last accepted report, named after the agent (e.g. <c>rsd-trust-agent.json</c>), as the
    /// baseline its scores mustn't fall below. Relative to the app's folder. An agent without one is held to
    /// <see cref="MinimumScore"/> only.
    /// </summary>
    public string BaselinesDirectory { get; set; } = "Baselines";

    /// <summary>
    /// The largest gap allowed, per metric, between the average scores of test case groups (each case's <c>group</c>,
    /// such as <c>special-school</c> or <c>academy</c>), so no kind of school or trust is served worse. Unset turns the
    /// check off.
    /// </summary>
    public double? MaxGroupGap { get; set; }

    /// <summary>Whether answers are scored by a judge model.</summary>
    public bool HasJudge => !string.IsNullOrWhiteSpace(JudgeModel);
}
