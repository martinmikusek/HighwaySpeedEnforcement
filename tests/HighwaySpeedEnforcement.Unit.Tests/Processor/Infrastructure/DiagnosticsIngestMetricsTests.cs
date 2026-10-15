using System.Diagnostics.Metrics;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Infrastructure;

public sealed class DiagnosticsIngestMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    [Trait("BR", "BR-ING-07")]
    public void Counters_count_received_accepted_rejected_by_reason_and_ignored()
    {
        using var listener = new RecordingListener();
        using var metrics = new DiagnosticsIngestMetrics(new TestMeterFactory(), () => Idle(), new FakeTimeProvider(Now));

        metrics.Received();
        metrics.Received();
        metrics.Accepted();
        metrics.Rejected(RejectReason.OutOfSequence);
        metrics.Rejected(RejectReason.OutOfSequence);
        metrics.Rejected(RejectReason.InvalidPlate);
        metrics.Ignored();

        Assert.Equal(2, listener.Sum("processor.ingest.received"));
        Assert.Equal(1, listener.Sum("processor.ingest.accepted"));
        Assert.Equal(2, listener.Sum("processor.ingest.rejected", ("reason", "OutOfSequence")));
        Assert.Equal(1, listener.Sum("processor.ingest.rejected", ("reason", "InvalidPlate")));
        Assert.Equal(3, listener.Sum("processor.ingest.rejected"));
        Assert.Equal(1, listener.Sum("processor.ingest.ignored"));

        var counters = metrics.Counters;
        Assert.Equal(2, counters.Received);
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(1, counters.Ignored);
        Assert.Equal(2, counters.Rejected[RejectReason.OutOfSequence]);
        Assert.Equal(1, counters.Rejected[RejectReason.InvalidPlate]);
        Assert.Equal(0, counters.Rejected[RejectReason.Malformed]);
        Assert.Equal(3, counters.RejectedTotal);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void Latency_is_recorded_in_a_histogram_in_seconds()
    {
        using var listener = new RecordingListener();
        using var metrics = new DiagnosticsIngestMetrics(new TestMeterFactory(), () => Idle(), new FakeTimeProvider(Now));

        metrics.Latency(TimeSpan.FromMilliseconds(300));
        metrics.Latency(TimeSpan.FromMilliseconds(150));

        Assert.Equal(0.45, listener.Sum("processor.ingest.latency"), 6);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void Gauges_read_the_ingest_status()
    {
        using var listener = new RecordingListener();
        var status = new IngestStatus(true, 17, [3, 0, 5], Now - TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), Now - TimeSpan.FromSeconds(9));
        using var metrics = new DiagnosticsIngestMetrics(new TestMeterFactory(), () => status, new FakeTimeProvider(Now));

        listener.Observe();

        Assert.Equal(17, listener.Sum("processor.ingest.tracked_vehicles"));
        Assert.Equal(3, listener.Sum("processor.ingest.buffer_depth", ("partition", 0)));
        Assert.Equal(0, listener.Sum("processor.ingest.buffer_depth", ("partition", 1)));
        Assert.Equal(5, listener.Sum("processor.ingest.buffer_depth", ("partition", 2)));
        Assert.Equal(2.0, listener.Sum("processor.ingest.lag_seconds"));
        Assert.Equal(9.0, listener.Sum("processor.ingest.seconds_since_last_received"));
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void Gauges_without_data_report_nothing()
    {
        using var listener = new RecordingListener();
        using var metrics = new DiagnosticsIngestMetrics(new TestMeterFactory(), () => Idle(), new FakeTimeProvider(Now));

        listener.Observe();

        Assert.Equal(0, listener.Sum("processor.ingest.tracked_vehicles"));
        Assert.Empty(listener.Measurements("processor.ingest.lag_seconds"));
        Assert.Empty(listener.Measurements("processor.ingest.seconds_since_last_received"));
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void Only_reason_and_partition_are_ever_tags()
    {
        using var listener = new RecordingListener();
        using var metrics = new DiagnosticsIngestMetrics(new TestMeterFactory(), () => Idle(), new FakeTimeProvider(Now));

        foreach (var reason in Enum.GetValues<RejectReason>())
        {
            metrics.Rejected(reason);
        }

        metrics.Received();
        metrics.Accepted();
        metrics.Ignored();
        listener.Observe();

        var tagKeys = listener.All.SelectMany(m => m.Tags.Select(t => t.Key)).Distinct().Order();
        Assert.Equal(["partition", "reason"], tagKeys);
    }

    private static IngestStatus Idle()
    {
        return new IngestStatus(true, 0, [0], null, null, null);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options)
        {
            return new Meter(options);
        }

        public void Dispose()
        {
        }
    }

    private sealed record Recorded(string Instrument, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags);

    private sealed class RecordingListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<Recorded> _recorded = [];

        public RecordingListener()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DiagnosticsIngestMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.Start();
        }

        public IReadOnlyList<Recorded> All => _recorded;

        public void Observe()
        {
            _listener.RecordObservableInstruments();
        }

        public IEnumerable<Recorded> Measurements(string instrument, params (string Key, object Value)[] tags)
        {
            return _recorded.Where(m => m.Instrument == instrument && tags.All(t => m.Tags.Any(x => x.Key == t.Key && Equals(x.Value, t.Value))));
        }

        public double Sum(string instrument, params (string Key, object Value)[] tags)
        {
            return Measurements(instrument, tags).Sum(m => m.Value);
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            _recorded.Add(new Recorded(instrument.Name, value, tags.ToArray()));
        }
    }
}
