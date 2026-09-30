using System.Diagnostics;
using System.Text.Json;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.Options;
using Dfe.Common.AI.Services.Application.QualityGate.Interfaces;
using Dfe.Common.AI.Services.Application.ValueObjects;
using GovUK.Dfe.CoreLibs.AiAgents.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Quality;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.QualityGate;

public sealed class AgentReleaseGate(
    IAgentTestRunner agentTestRunner,
    IAgentRuntime agentRuntime,
    AgentQualityOptions agentQualityOptions,
    ILogger<AgentReleaseGate> logger) : IAgentReleaseGate
{
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(1);

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

        return new TestSuite(definition, directory, cases);
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
            var result = await RunCaseAsync(run.Suite.Definition, run.Suite.Cases[run.Index], token);
            run.Suite.Results[run.Index] = result;
        });
    }

    private async Task<AgentTestResult> RunCaseAsync(AgentDefinition definition, AgentTestCase testCase,
        CancellationToken cancellationToken)
    {
        // The candidate is a temporary copy of the agent, so a Foundry version is only created if the agent passes.
        var report = await agentTestRunner.RunAsync(definition, [testCase], AgentTestTarget.Candidate, cancellationToken);
        var result = report.Results[0];

        if (result.Passed)
        {
            logger.LogInformation(Messages.Log.TestCasePassed, definition.Name, result.CaseName);
        }
        else
        {
            logger.LogWarning(Messages.Log.TestCaseFailed, definition.Name, result.CaseName, string.Join("; ", result.Failures));
        }

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

        var problems = report.Results
            .Where(r => !r.Passed)
            .Select(r => Messages.ReleaseGate.TestCaseFailed(r.CaseName, r.Failures))
            .ToList();

        if (agentQualityOptions.HasJudge)
        {
            // Cases expecting a refusal are checked by their required refusal instead: the judge's relevance score
            // marks any refusal as irrelevant, however correct.
            var answered = new AgentEvaluationReport(agentName,
                [.. suite.Results.Where((_, index) => !FixedResponses.ExpectsRefusal(suite.Cases[index]))]);
            logger.LogInformation(Messages.Log.AgentScores, agentName, Messages.ReleaseGate.Scores(answered.AverageScores),
                suite.Cases.Count - answered.Results.Count);
            problems.AddRange(ScoreProblems(answered));
        }

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

    /// <summary>A metric scored below the minimum, or not scored at all, is a problem.</summary>
    private IEnumerable<string> ScoreProblems(AgentEvaluationReport report)
    {
        var minimum = agentQualityOptions.MinimumScore;
        var scores = report.AverageScores;

        return report.BelowMinimum(minimum, [.. JudgeMetrics.All])
            .Select(metric => scores.TryGetValue(metric, out var score)
                ? Messages.ReleaseGate.ScoreBelowMinimum(metric, score, minimum)
                : Messages.ReleaseGate.NotScored(metric));
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
            await agentRuntime.DeleteOrphanedEphemeralAgentsAsync(timeout.Token);
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

    /// <summary>One agent's test cases, and their results in the same order once they have run.</summary>
    private sealed class TestSuite(AgentDefinition definition, string directory, IReadOnlyList<AgentTestCase> cases)
    {
        public AgentDefinition Definition { get; } = definition;

        public string Directory { get; } = directory;

        public IReadOnlyList<AgentTestCase> Cases { get; } = cases;

        public AgentTestResult[] Results { get; } = new AgentTestResult[cases.Count];
    }
}
