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

        var results = new List<AgentGateResult>(definitions.Count);
        try
        {
            foreach (var definition in definitions)
            {
                results.Add(await TestAgentAsync(definition, cancellationToken));
            }
        }
        finally
        {
            await DeleteLeftoverTestAgentsAsync();
        }

        return results;
    }

    private async Task<AgentGateResult> TestAgentAsync(AgentDefinition definition, CancellationToken cancellationToken)
    {
        var casesDirectory = Path.Combine(AppContext.BaseDirectory, agentQualityOptions.TestCasesDirectory, definition.Name);
        var cases = Directory.Exists(casesDirectory)
            ? await AgentTestCase.LoadAsync(casesDirectory, cancellationToken)
            : [];
        if (cases.Count == 0)
        {
            logger.LogWarning(Messages.Log.NoTestCases, definition.Name, casesDirectory);
            return new AgentGateResult(definition.Name, [Messages.ReleaseGate.NoTestCases(casesDirectory)]);
        }

        // The candidate is a temporary copy of the agent, so a Foundry version is only created if the agent passes.
        logger.LogInformation(Messages.Log.TestingAgent, definition.Name, cases.Count);
        var report = await agentTestRunner.RunAsync(definition, cases, AgentTestTarget.Candidate, cancellationToken);
        var reportPath = await WriteReportAsync(report, cancellationToken);
        LogTestCases(report);

        var problems = report.Results
            .Where(r => !r.Passed)
            .Select(r => Messages.ReleaseGate.TestCaseFailed(r.CaseName, r.Failures))
            .ToList();

        if (agentQualityOptions.HasJudge)
        {
            logger.LogInformation(Messages.Log.AgentScores, definition.Name, Messages.ReleaseGate.Scores(report.AverageScores));
            problems.AddRange(ScoreProblems(report));
        }

        if (problems.Count == 0)
        {
            logger.LogInformation(Messages.Log.AgentPassed, definition.Name, reportPath);
        }
        else
        {
            logger.LogWarning(Messages.Log.AgentFailed, definition.Name, problems.Count, reportPath);
        }

        return new AgentGateResult(definition.Name, problems);
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

    private void LogTestCases(AgentEvaluationReport report)
    {
        foreach (var result in report.Results)
        {
            if (result.Passed)
            {
                logger.LogInformation(Messages.Log.TestCasePassed, report.AgentName, result.CaseName);
            }
            else
            {
                logger.LogWarning(Messages.Log.TestCaseFailed, report.AgentName, result.CaseName, string.Join("; ", result.Failures));
            }
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
}
