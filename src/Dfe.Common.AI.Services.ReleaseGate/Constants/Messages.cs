using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Reporting;

namespace Dfe.Common.AI.Services.ReleaseGate.Constants;

/// <summary>The release gate's messages. The library logs the checks themselves (see README).</summary>
public static class Messages
{
    public static class Errors
    {
        /// <summary>
        /// Why nothing was provisioned: one line per reason for each blocked agent, as the quality gate gives them, and
        /// where its report is.
        /// </summary>
        public static string ReleaseBlocked(QualityGateReport report)
        {
            ArgumentNullException.ThrowIfNull(report);

            var lines = report.Agents.Where(static agent => !agent.ReleasePermitted)
                .SelectMany(static agent => agent.Reasons.Select(reason => $"- {agent.AgentName}: {reason}"));
            var message = "No agents were provisioned because the quality gate blocked their release:" + Environment.NewLine +
                string.Join(Environment.NewLine, lines);
            return report.MarkdownPath is null ? message : message + Environment.NewLine + $"Report: {report.MarkdownPath}";
        }
    }
}
