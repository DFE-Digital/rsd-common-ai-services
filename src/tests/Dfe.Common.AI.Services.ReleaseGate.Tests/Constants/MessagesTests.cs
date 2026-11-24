using Dfe.Common.AI.Services.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Reporting;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Traceability;
using GovUK.Dfe.AI.Agents.Evaluation.ValueObjects;

namespace Dfe.Common.AI.Services.ReleaseGate.Tests.Constants;

public sealed class MessagesTests
{
    [Fact]
    public void A_blocked_release_lists_each_blocked_agents_reasons_and_the_report()
    {
        var report = new QualityGateReport
        {
            Decision = ReleaseDecision.Fail,
            ReleasePermitted = false,
            Reasons = ["rsd-trust-agent: Fail"],
            Manifest = new ReleaseManifest
            {
                RunId = "run", ApplicationName = "app", GeneratedAt = DateTimeOffset.UnixEpoch,
                EvaluationFrameworkVersion = "1", PolicyFingerprint = "policy",
            },
            Agents =
            [
                Decision("rsd-establishment-agent", permitted: true, "Pass"),
                Decision("rsd-trust-agent", permitted: false,
                    "Fail: 1 blocking finding", "guardrail/privacy-and-security/PRIV-003: Not blocked by the guardrail."),
            ],
            MarkdownPath = "evaluation-reports/quality-gate-report.md",
        };

        var lines = Messages.Errors.ReleaseBlocked(report).Split(Environment.NewLine);

        Assert.Equal(
            [
                "No agents were provisioned because the quality gate blocked their release:",
                "- rsd-trust-agent: Fail: 1 blocking finding",
                "- rsd-trust-agent: guardrail/privacy-and-security/PRIV-003: Not blocked by the guardrail.",
                "Report: evaluation-reports/quality-gate-report.md",
            ],
            lines);
    }

    private static AgentReleaseDecision Decision(string agent, bool permitted, params string[] reasons) => new()
    {
        AgentName = agent,
        Decision = permitted ? ReleaseDecision.Pass : ReleaseDecision.Fail,
        ReleasePermitted = permitted,
        Reasons = reasons,
    };
}
