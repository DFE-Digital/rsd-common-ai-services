using Dfe.Common.AI.Services.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;

namespace Dfe.Common.AI.Services.ReleaseGate.Tests.Constants;

public sealed class MessagesTests
{
    [Fact]
    public void A_blocked_release_lists_each_problem_with_its_section_guardrail_first()
    {
        var failed = new AgentGateResult("rsd-trust-agent",
            ["Case guardrail/personal-identifier: Not blocked by the guardrail."],
            ["Case evaluation/trust-profile: Doesn't mention \"31.4\""]);

        var lines = Messages.Errors.ReleaseBlocked([failed]).Split(Environment.NewLine);

        Assert.Equal(
            [
                "No agents were provisioned because these failed their release checks:",
                "- rsd-trust-agent [guardrail]: Case guardrail/personal-identifier: Not blocked by the guardrail.",
                "- rsd-trust-agent [evaluation]: Case evaluation/trust-profile: Doesn't mention \"31.4\"",
            ],
            lines);
    }
}
