using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using GovUK.Dfe.AI.Agents.Diagnostics;

namespace Dfe.Common.AI.Services.Application.Tests.Helpers;

/// <summary>
/// The tags of what's recorded on one of the agents library's metrics. Tests running side by side record on the same
/// metrics, so assertions look for their own release run's measurements rather than counting them all.
/// </summary>
internal sealed class MetricListener : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> _recorded = new();

    public MetricListener(string metricName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AgentTelemetry.SourceName && instrument.Name == metricName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            _recorded.Enqueue(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
        _listener.Start();
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Recorded => [.. _recorded];

    public void Dispose() => _listener.Dispose();
}
