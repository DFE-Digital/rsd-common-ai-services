using Dfe.Common.AI.Services.ReleaseGate.Metrics;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Dfe.Common.AI.Services.ReleaseGate.Tests.Commons;

/// <summary>
/// The guardrail checks recorded on this repo's meter. Other tests can record at the same time, so assertions look for
/// these agents' checks rather than counting them all.
/// </summary>
internal sealed class GuardrailCheckListener : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentBag<(string Agent, string Outcome)> _checks = [];

    public GuardrailCheckListener()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ReleaseGateMetrics.MeterName && instrument.Name == "dfe.common_ai_services.guardrail.checks")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var tagList = tags.ToArray();
            string Tag(string key) => tagList.Single(tag => tag.Key == key).Value as string ?? string.Empty;
            _checks.Add((Tag(ReleaseGateMetrics.AgentNameTag), Tag("dfe.ai_agents.release_gate.outcome")));
        });
        _listener.Start();
    }

    public IReadOnlyCollection<(string Agent, string Outcome)> Checks => _checks;

    public void Dispose() => _listener.Dispose();
}
