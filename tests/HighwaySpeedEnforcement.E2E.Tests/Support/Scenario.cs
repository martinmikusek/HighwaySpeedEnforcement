using HighwaySpeedEnforcement.Processor.Contracts.Rest;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>
/// One scripted run: a fresh processor container, a publisher that stands in for the generator, and the oracle fed with
/// exactly the same measurements. Timestamps are synthetic event time in the past of the processor clock, so a scenario
/// spanning seconds of event time finishes immediately (testing strategy).
/// </summary>
public sealed class Scenario : IAsyncDisposable
{
    private readonly ProcessorContainer _processor;
    private readonly TestPublisher _publisher;
    private long _sent;

    private Scenario(ProcessorContainer processor, TestPublisher publisher, ProcessorRun settings)
    {
        _processor = processor;
        _publisher = publisher;
        Oracle = new BoardOracle(settings);
        Origin = DateTimeOffset.UtcNow.AddHours(-1);
    }

    public BoardOracle Oracle { get; }

    /// <summary>Event-time zero of the scenario.</summary>
    public DateTimeOffset Origin { get; }

    public ProcessorContainer Processor => _processor;

    public static async Task<Scenario> StartAsync(PipelineStack stack, ProcessorRun settings, CancellationToken cancellationToken)
    {
        var processor = await stack.StartProcessorAsync(settings, cancellationToken);
        var publisher = await TestPublisher.ConnectAsync(stack.Broker, TelemetryContract.Topic, cancellationToken);
        return new Scenario(processor, publisher, settings);
    }

    /// <summary>Ticks of <see cref="Origin"/> plus <paramref name="seconds"/>.</summary>
    public long At(double seconds)
    {
        return Origin.UtcTicks + (long)(seconds * TimeSpan.TicksPerSecond);
    }

    public DateTimeOffset AtTime(double seconds)
    {
        return new DateTimeOffset(At(seconds), TimeSpan.Zero);
    }

    /// <summary>Publishes in order, feeds the oracle, and waits until the processor applied everything sent so far.</summary>
    public async Task PublishAsync(IEnumerable<Telemetry> measurements, CancellationToken cancellationToken)
    {
        foreach (var m in measurements)
        {
            await _publisher.PublishMeasurementAsync(m.Plate, m.Camera, m.Ticks, m.Speed, cancellationToken);
            Oracle.Apply(m);
            _sent++;
        }

        await _processor.WaitUntilAppliedAsync(_sent, cancellationToken);
    }

    public Task PublishAsync(CancellationToken cancellationToken, params Telemetry[] measurements)
    {
        return PublishAsync(measurements, cancellationToken);
    }

    public Task<TrafficSnapshotDto> SnapshotAsync(CancellationToken cancellationToken)
    {
        return _processor.GetSnapshotAsync(cancellationToken);
    }

    /// <summary>The snapshot's camera board must equal the oracle's live top 10 for that camera.</summary>
    public void AssertCameraBoard(TrafficSnapshotDto snapshot, int camera)
    {
        var board = snapshot.CameraBoards[camera - 1];
        Assert.Equal(camera, board.CameraId);
        Assert.Equal(ToTime(Oracle.CameraWatermark(camera)), board.LatestEventUtc);
        Assert.Equal(
            Oracle.CameraTop(camera),
            board.Entries.Select(entry => new ExpectedCameraEntry(entry.Rank, entry.NumberPlate, entry.SpeedKmh, entry.MeasuredAtUtc.UtcTicks)));
    }

    /// <summary>The snapshot's global board must equal the oracle's live top 10.</summary>
    public void AssertGlobalBoard(TrafficSnapshotDto snapshot)
    {
        Assert.Equal(ToTime(Oracle.GlobalWatermark), snapshot.GlobalBoard.LatestEventUtc);
        var expected = Oracle.GlobalTop();
        var actual = snapshot.GlobalBoard.Entries.Select(entry => new ExpectedGlobalEntry(entry.Rank, entry.NumberPlate, entry.AverageSpeedKmh, entry.MeasurementCount, entry.MeasuredAtUtc.UtcTicks)).ToArray();

        Assert.Equal(expected.Select(e => e with { Average = 0 }), actual.Select(e => e with { Average = 0 }));
        Assert.All(expected.Zip(actual), pair => Assert.Equal(pair.First.Average, pair.Second.Average, 1e-9));
    }

    public void AssertAllBoards(TrafficSnapshotDto snapshot)
    {
        for (var camera = 1; camera <= 10; camera++)
        {
            AssertCameraBoard(snapshot, camera);
        }

        AssertGlobalBoard(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        await _publisher.DisposeAsync();
        await _processor.DisposeAsync();
    }

    private static DateTimeOffset? ToTime(long? ticks)
    {
        return ticks is { } value ? new DateTimeOffset(value, TimeSpan.Zero) : null;
    }
}
