using HighwaySpeedEnforcement.Processor.Application;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Application;

/// <summary>The latency of a message: the time from its event time to its processing (BR-OPS-02), without the saw tooth of the ingest lag.</summary>
public sealed class LatencyTests
{
    private static readonly long Second = TimeSpan.TicksPerSecond;

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void The_window_is_empty_until_something_is_recorded()
    {
        var window = new LatencyWindow(new FakeTimeProvider(PipelineHarness.Now));

        Assert.Null(window.Snapshot());
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void The_window_reports_count_average_and_maximum()
    {
        var window = new LatencyWindow(new FakeTimeProvider(PipelineHarness.Now));

        window.Record(TimeSpan.FromMilliseconds(100).Ticks);
        window.Record(TimeSpan.FromMilliseconds(300).Ticks);
        window.Record(TimeSpan.FromMilliseconds(200).Ticks);

        var stats = window.Snapshot()!;
        Assert.Equal(3, stats.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(200), stats.Average);
        Assert.Equal(TimeSpan.FromMilliseconds(300), stats.Maximum);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void Samples_leave_the_window_after_ten_seconds()
    {
        var time = new FakeTimeProvider(PipelineHarness.Now);
        var window = new LatencyWindow(time);
        window.Record(TimeSpan.FromSeconds(1).Ticks);

        time.Advance(TimeSpan.FromSeconds(9));
        window.Record(TimeSpan.FromMilliseconds(100).Ticks);
        Assert.Equal(2, window.Snapshot()!.Count);

        time.Advance(TimeSpan.FromSeconds(1));
        var afterTen = window.Snapshot()!;
        Assert.Equal(1, afterTen.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), afterTen.Maximum);

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.Null(window.Snapshot());
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public void A_bucket_is_reused_after_a_full_window_without_keeping_the_old_samples()
    {
        var time = new FakeTimeProvider(PipelineHarness.Now);
        var window = new LatencyWindow(time);
        window.Record(TimeSpan.FromSeconds(5).Ticks);

        time.Advance(TimeSpan.FromSeconds(LatencyWindow.WindowSeconds));
        window.Record(TimeSpan.FromMilliseconds(2).Ticks);

        var stats = window.Snapshot()!;
        Assert.Equal(1, stats.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(2), stats.Maximum);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task The_pipeline_reports_the_latency_from_the_event_time_to_the_processing()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 1 });
        Assert.Null(harness.Pipeline.Status.Latency);

        await harness.SubmitAndDrainAsync(
            new IncomingMeasurement("A", PipelineHarness.Now.UtcTicks - TimeSpan.FromMilliseconds(300).Ticks, 130f, 1),
            new IncomingMeasurement("B", PipelineHarness.Now.UtcTicks - TimeSpan.FromMilliseconds(100).Ticks, 130f, 1));

        var latency = harness.Pipeline.Status.Latency!;
        Assert.Equal(2, latency.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(200), latency.Average);
        Assert.Equal(TimeSpan.FromMilliseconds(300), latency.Maximum);
        Assert.Equal([TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(100)], harness.Metrics.Latencies);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task The_latency_does_not_saw_tooth_between_bursts_but_the_ingest_lag_does()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 1 });
        long eventTime = PipelineHarness.Now.UtcTicks - TimeSpan.FromMilliseconds(150).Ticks;
        await harness.SubmitAndDrainAsync(new IncomingMeasurement("A", eventTime, 130f, 1));

        var right = harness.Pipeline.Status;
        harness.Time.Advance(TimeSpan.FromMilliseconds(900));
        var late = harness.Pipeline.Status;

        Assert.Equal(TimeSpan.FromMilliseconds(150), right.IngestLag);
        Assert.Equal(TimeSpan.FromMilliseconds(1_050), late.IngestLag);
        Assert.Equal(right.Latency, late.Latency);
        Assert.Equal(TimeSpan.FromMilliseconds(150), late.Latency!.Average);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task Heartbeats_count_and_an_event_ahead_of_the_clock_counts_as_zero_while_rejected_messages_do_not_count()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 1 });

        await harness.SubmitAndDrainAsync(
            new IncomingHeartbeat(5, PipelineHarness.Now.UtcTicks - (2 * Second)),
            new IncomingMeasurement("A", PipelineHarness.Now.UtcTicks + Second, 130f, 1),
            new IncomingMeasurement("B", PipelineHarness.Now.UtcTicks, -1f, 1));

        var latency = harness.Pipeline.Status.Latency!;
        Assert.Equal(2, latency.Count);
        Assert.Equal(TimeSpan.FromSeconds(2), latency.Maximum);
        Assert.Equal(TimeSpan.FromSeconds(1), latency.Average);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.Zero], harness.Metrics.Latencies);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task The_pipeline_forgets_the_latency_after_ten_quiet_seconds()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 1 });
        await harness.SubmitAndDrainAsync(new IncomingMeasurement("A", PipelineHarness.Now.UtcTicks - TimeSpan.FromMilliseconds(50).Ticks, 130f, 1));
        Assert.NotNull(harness.Pipeline.Status.Latency);

        harness.Time.Advance(TimeSpan.FromSeconds(LatencyWindow.WindowSeconds + 1));

        Assert.Null(harness.Pipeline.Status.Latency);
    }
}
