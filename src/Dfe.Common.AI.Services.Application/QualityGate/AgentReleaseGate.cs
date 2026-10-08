using System.Diagnostics;
using System.Text.Json;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Diagnostics;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate.Interfaces;
using Dfe.Common.AI.Services.Application.ValueObjects;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Enums;
using GovUK.Dfe.AI.Agents.Quality;
using GovUK.Dfe.AI.Agents.Quality.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.QualityGate;

public sealed class AgentReleaseGate(
    IAgentTestRunner agentTestRunner,
    IAgentRuntimeService agentRuntime,
    AgentQualityOptions agentQualityOptions,
    ILogger<AgentReleaseGate> logger) : IAgentReleaseGate
{
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(1);

    private static readonly IReadOnlyDictionary<string, double> NoScores = new Dictionary<string, double>();

    public async Task<IReadOnlyList<AgentGateResult>> EvaluateAsync(IReadOnlyCollection<AgentDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        LogJudge();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var suites = new List<TestSuite>(definitions.Count);
            foreach (var definition in definitions)
            {
                suites.Add(await LoadSuiteAsync(definition, cancellationToken));
            }

            await RunAllCasesAsync(suites, cancellationToken);

            var results = new List<AgentGateResult>(suites.Count);
            foreach (var suite in suites)
            {
                results.Add(await ConcludeAsync(suite, cancellationToken));
            }

            logger.LogInformation(Messages.Log.ReleaseChecksFinished, Stopwatch.GetElapsedTime(started).TotalSeconds);
            return results;
        }
        finally
        {
            await DeleteLeftoverTestAgentsAsync();
        }
    }

    private async Task<TestSuite> LoadSuiteAsync(AgentDefinition definition, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, agentQualityOptions.TestCasesDirectory, definition.Name);
        var cases = Directory.Exists(directory) ? await AgentTestCase.LoadAsync(directory, cancellationToken) : [];
        if (cases.Count > 0)
        {
            logger.LogInformation(Messages.Log.TestingAgent, definition.Name, cases.Count);
        }

        var mayBeBlocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testCase in cases)
        {
            if (await GuardrailBlocks.AllowedForAsync(Path.Combine(directory, $"{testCase.Name}.json"), cancellationToken))
            {
                mayBeBlocked.Add(testCase.Name);
            }
        }

        return new TestSuite(definition, directory, cases, mayBeBlocked);
    }

    /// <summary>
    /// Runs every agent's test cases side by side, at most <see cref="AgentQualityOptions.MaxParallelTestRuns"/> at a
    /// time. Each case runs on its own temporary copy of its agent, so cases don't affect each other.
    /// </summary>
    private async Task RunAllCasesAsync(IReadOnlyList<TestSuite> suites, CancellationToken cancellationToken)
    {
        var runs = suites.SelectMany(suite => suite.Cases.Select((_, index) => (Suite: suite, Index: index))).ToList();
        var maxParallel = Math.Max(1, agentQualityOptions.MaxParallelTestRuns);
        logger.LogInformation(Messages.Log.RunningTestCases, runs.Count, maxParallel);

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = maxParallel, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(runs, parallel, async (run, token) =>
        {
            var testCase = run.Suite.Cases[run.Index];
            run.Suite.Results[run.Index] = await RunCaseAsync(
                run.Suite.Definition, testCase, run.Suite.MayBeBlocked.Contains(testCase.Name), token);
        });
    }

    private async Task<AgentTestResult> RunCaseAsync(AgentDefinition definition, AgentTestCase testCase,
        bool mayBeBlocked, CancellationToken cancellationToken)
    {
        // The candidate is a temporary copy of the agent, so a Foundry version is only created if the agent passes.
        // Repeats average out the judge's run-to-run noise; the case's facts must be right in every repeat.
        var report = await agentTestRunner.RunAsync(definition, [testCase], AgentTestTarget.Candidate,
            Math.Max(1, agentQualityOptions.Repeats), cancellationToken);
        var result = report.Results[0];

        if (mayBeBlocked && GuardrailBlocks.IsBlocked(result))
        {
            logger.LogInformation(Messages.Log.TestCaseBlockedByGuardrail, definition.Name, result.CaseName);
            ReleaseGateMetrics.RecordTestCase(definition.Name, ReleaseGateMetrics.BlockedByGuardrail);
            return result with { Output = Messages.ReleaseGate.BlockedByGuardrail, Failures = [] };
        }

        if (result.Passed)
        {
            logger.LogInformation(Messages.Log.TestCasePassed, definition.Name, result.CaseName);
        }
        else
        {
            logger.LogWarning(Messages.Log.TestCaseFailed, definition.Name, result.CaseName, string.Join("; ", result.Failures));
        }

        ReleaseGateMetrics.RecordTestCase(definition.Name, result.Passed ? ReleaseGateMetrics.Passed : ReleaseGateMetrics.Failed);
        return result;
    }

    private async Task<AgentGateResult> ConcludeAsync(TestSuite suite, CancellationToken cancellationToken)
    {
        var agentName = suite.Definition.Name;
        if (suite.Cases.Count == 0)
        {
            logger.LogWarning(Messages.Log.NoTestCases, agentName, suite.Directory);
            return new AgentGateResult(agentName, [Messages.ReleaseGate.NoTestCases(suite.Directory)]);
        }

        var report = new AgentEvaluationReport(agentName, suite.Results);
        var reportPath = await WriteReportAsync(report, cancellationToken);

        var gated = report;
        AgentEvaluationReport? baseline = null;
        if (agentQualityOptions.HasJudge)
        {
            // Cases expecting a refusal keep their checks but not their scores: the judge's relevance score marks any
            // refusal as irrelevant, however correct, so they're checked by their required refusal instead.
            var refusals = suite.Cases.Where(FixedResponses.ExpectsRefusal).Select(c => c.Name).ToHashSet();
            gated = WithoutScoresFor(report, refusals);
            logger.LogInformation(Messages.Log.AgentScores, agentName, Messages.ReleaseGate.Scores(gated.AverageScores),
                refusals.Count);
            ReleaseGateMetrics.RecordScores(agentName, gated.AverageScores);

            baseline = await LoadBaselineAsync(agentName, cancellationToken) is { } saved
                ? WithoutScoresFor(saved, refusals)
                : null;
        }

        // Failed test cases, and with a judge: each metric averaging below the minimum, not scored, more than the
        // tolerance below the baseline, or scoring one group of cases much worse than another.
        var problems = gated.FailuresAgainst(Gate(), baseline);
        ReleaseGateMetrics.RecordAgent(agentName, problems.Count == 0);
        if (problems.Count == 0)
        {
            logger.LogInformation(Messages.Log.AgentPassed, agentName, reportPath);
        }
        else
        {
            logger.LogWarning(Messages.Log.AgentFailed, agentName, problems.Count, reportPath);
        }

        return new AgentGateResult(agentName, problems);
    }

    private void LogJudge()
    {
        if (agentQualityOptions.HasJudge)
        {
            logger.LogInformation(Messages.Log.JudgeOn, agentQualityOptions.JudgeModel, agentQualityOptions.MinimumScore);
        }
        else
        {
            logger.LogInformation(Messages.Log.JudgeOff);
        }
    }

    /// <summary>The scores each agent must reach.</summary>
    private ReleaseGate Gate() => new()
    {
        Metrics = agentQualityOptions.HasJudge ? JudgeMetrics.All : [],
        MinimumScore = agentQualityOptions.MinimumScore,
        Tolerance = agentQualityOptions.Tolerance,
        MaxGroupGap = agentQualityOptions.MaxGroupGap,
    };

    private static AgentEvaluationReport WithoutScoresFor(AgentEvaluationReport report, IReadOnlySet<string> caseNames) =>
        report with
        {
            Results = [.. report.Results.Select(result => caseNames.Contains(result.CaseName) ? result with { Scores = NoScores } : result)],
        };

    /// <summary>
    /// The agent's last accepted report, if one has been saved to <see cref="AgentQualityOptions.BaselinesDirectory"/>.
    /// An agent without one is held to the minimum score only.
    /// </summary>
    private async Task<AgentEvaluationReport?> LoadBaselineAsync(string agentName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, agentQualityOptions.BaselinesDirectory, $"{agentName}.json");
        if (!File.Exists(path))
        {
            logger.LogInformation(Messages.Log.NoBaseline, agentName, path);
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var baseline = await JsonSerializer.DeserializeAsync<AgentEvaluationReport>(stream, ReportJson, cancellationToken);
            logger.LogInformation(Messages.Log.ComparingWithBaseline, agentName, path);
            return baseline;
        }
        catch (JsonException ex)
        {
            throw new AgentConfigurationException(Messages.Errors.BaselineUnreadable(path, ex.Message), ex);
        }
    }

    /// <summary>
    /// Each test agent is deleted straight after its run, even when its test fails. This also removes any left by an
    /// earlier run whose delete failed or that was killed. It has its own timeout so it still runs when the gate was
    /// cancelled, and a failure here never hides the gate's own outcome; the next run tries again.
    /// </summary>
    private async Task DeleteLeftoverTestAgentsAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(CleanupTimeout);
            await agentRuntime.DeleteOrphanedEphemeralAgentsAsync(cancellationToken: timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, Messages.Log.LeftoverTestAgentsNotDeleted);
        }
    }

    /// <returns>Where the report was written.</returns>
    private async Task<string> WriteReportAsync(AgentEvaluationReport report, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(agentQualityOptions.ReportsDirectory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{report.AgentName}.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, ReportJson, cancellationToken);
        return path;
    }

    /// <summary>
    /// One agent's test cases, which of them a guardrail block counts as passing, and their results in the same order
    /// once they have run.
    /// </summary>
    private sealed class TestSuite(
        AgentDefinition definition, string directory, IReadOnlyList<AgentTestCase> cases, IReadOnlySet<string> mayBeBlocked)
    {
        public AgentDefinition Definition { get; } = definition;

        public string Directory { get; } = directory;

        public IReadOnlyList<AgentTestCase> Cases { get; } = cases;

        public IReadOnlySet<string> MayBeBlocked { get; } = mayBeBlocked;

        public AgentTestResult[] Results { get; } = new AgentTestResult[cases.Count];
    }
}
