using System.Diagnostics.Metrics;
using HighwaySpeedEnforcement.Processor.Application;

namespace HighwaySpeedEnforcement.Processor.Infrastructure;

/// <summary>
/// <see cref="IIngestMetrics"/> on <c>System.Diagnostics.Metrics</c> (BR-OPS-02): counters for received, accepted, rejected
/// (tagged <c>reason</c>) and ignored messages, and observable gauges read from the pipeline's <see cref="IngestStatus"/>.
/// The status is read lazily through a delegate because the pipeline itself depends on the metrics. The same totals are
/// kept as plain numbers (<see cref="Counters"/>) for the status endpoint. Plates are never tags.
/// </summary>
public sealed class DiagnosticsIngestMetrics : IIngestMetrics, IDisposable
{
    public const string MeterName = "HighwaySpeedEnforcement.Processor";
    public const string ReasonTag = "reason";
    public const string PartitionTag = "partition";

    private readonly Meter _meter;
    private readonly Counter<long> _received;
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _ignored;
    private readonly Histogram<double> _latency;
    private readonly KeyValuePair<string, object?>[] _reasonTags;
    private readonly long[] _rejectedTotals;
    private long _receivedTotal;
    private long _acceptedTotal;
    private long _ignoredTotal;

    public DiagnosticsIngestMetrics(IMeterFactory meterFactory, Func<IngestStatus> status, TimeProvider timeProvider)
    {
        _meter = meterFactory.Create(MeterName);
        _received = _meter.CreateCounter<long>("processor.ingest.received", "{message}", "Messages handed to the pipeline, before validation.");
        _accepted = _meter.CreateCounter<long>("processor.ingest.accepted", "{message}", "Measurements that changed state.");
        _rejected = _meter.CreateCounter<long>("processor.ingest.rejected", "{message}", "Rejected messages by reason.");
        _ignored = _meter.CreateCounter<long>("processor.ingest.ignored", "{message}", "Supported but disabled message types.");
        _latency = _meter.CreateHistogram(
            "processor.ingest.latency",
            "s",
            "Wall-clock time from the event time of an applied message to its processing.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10] });
        _reasonTags = Enum.GetValues<RejectReason>()
            .Select(reason => new KeyValuePair<string, object?>(ReasonTag, reason.ToString()))
            .ToArray();
        _rejectedTotals = new long[_reasonTags.Length];

        _meter.CreateObservableGauge("processor.ingest.tracked_vehicles", () => status().TrackedVehicles, "{vehicle}", "In-flight vehicles with a track.");
        _meter.CreateObservableGauge(
            "processor.ingest.buffer_depth",
            () => status().PartitionDepths.Select((depth, index) => new Measurement<int>(depth, new KeyValuePair<string, object?>(PartitionTag, index))),
            "{message}",
            "Queued items per partition.");
        _meter.CreateObservableGauge(
            "processor.ingest.lag_seconds",
            () => status().IngestLag is { } lag ? [new Measurement<double>(lag.TotalSeconds)] : Array.Empty<Measurement<double>>(),
            "s",
            "Wall-clock time minus the newest processed event time.");
        _meter.CreateObservableGauge(
            "processor.ingest.seconds_since_last_received",
            () => status().LastReceivedAt is { } last ? [new Measurement<double>((timeProvider.GetUtcNow() - last).TotalSeconds)] : Array.Empty<Measurement<double>>(),
            "s",
            "Time since the last message was received.");
    }

    /// <summary>The totals since start as plain numbers, for <c>GET /api/status</c>.</summary>
    public IngestCounters Counters
    {
        get
        {
            var rejected = new Dictionary<RejectReason, long>();

            foreach (var reason in Enum.GetValues<RejectReason>())
            {
                rejected[reason] = Volatile.Read(ref _rejectedTotals[(int)reason]);
            }

            return new IngestCounters(Volatile.Read(ref _receivedTotal), Volatile.Read(ref _acceptedTotal), Volatile.Read(ref _ignoredTotal), rejected);
        }
    }

    public void Received()
    {
        Interlocked.Increment(ref _receivedTotal);
        _received.Add(1);
    }

    public void Accepted()
    {
        Interlocked.Increment(ref _acceptedTotal);
        _accepted.Add(1);
    }

    public void Rejected(RejectReason reason)
    {
        Interlocked.Increment(ref _rejectedTotals[(int)reason]);
        _rejected.Add(1, _reasonTags[(int)reason]);
    }

    public void Ignored()
    {
        Interlocked.Increment(ref _ignoredTotal);
        _ignored.Add(1);
    }

    public void Latency(TimeSpan latency)
    {
        _latency.Record(latency.TotalSeconds);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
