using Microsoft.Extensions.AI.Evaluation.Quality;

namespace Dfe.Common.AI.Services.Application.QualityGate;

/// <summary>
/// The metrics the library's judge scores every answer on. The release gate requires a score for each.
/// </summary>
public static class JudgeMetrics
{
    public static IReadOnlyList<string> All { get; } =
        [GroundednessEvaluator.GroundednessMetricName, RelevanceEvaluator.RelevanceMetricName];
}
