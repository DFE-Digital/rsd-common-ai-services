using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;

namespace Dfe.Common.AI.Services.ReleaseGate.Constants;

/// <summary>The release gate's messages. The library logs the checks themselves (see README).</summary>
public static class Messages
{
    public static class Errors
    {
        /// <summary>
        /// Why nothing was provisioned: one line per problem, each marked with its section, as the library logs it
        /// (<see cref="AgentGateResult.ProblemLines"/>).
        /// </summary>
        public static string ReleaseBlocked(IEnumerable<AgentGateResult> failed) =>
            "No agents were provisioned because these failed their release checks:" + Environment.NewLine +
            string.Join(Environment.NewLine, failed.SelectMany(static result => result.ProblemLines()));
    }
}
