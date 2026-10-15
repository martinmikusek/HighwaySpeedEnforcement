using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.Infrastructure;
using HighwaySpeedEnforcement.E2E.Tests.Support;

namespace HighwaySpeedEnforcement.E2E.Tests.Client;

/// <summary>Step 11 of the client plan: the client's HTTP adapter against a real processor container fed by the test publisher.</summary>
[Collection(PipelineCollection.Name)]
[Trait("Category", "E2E")]
public sealed class HttpSnapshotClientE2ETests
{
    private readonly PipelineStack _stack;

    public HttpSnapshotClientE2ETests(PipelineStack stack)
    {
        _stack = stack;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "BR-API-01")]
    [Trait("BR", "BR-UI-01")]
    public async Task Real_snapshot_maps_to_ten_ranked_boards()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var vehicles = new[] { ("A", 150f), ("B", 160f), ("C", 140f) };
        var measurements = vehicles.SelectMany(vehicle => Enumerable.Range(1, 3).Select(camera => new Telemetry(vehicle.Item1, camera, scenario.At(camera * 0.1), vehicle.Item2)));
        await scenario.PublishAsync(measurements, Ct);
        using var http = new HttpClient { BaseAddress = scenario.Processor.BaseAddress };
        var client = new HttpSnapshotClient(http);

        var snapshot = await client.GetSnapshotAsync(Ct);

        Assert.Equal(TimeSpan.Zero, snapshot.GeneratedAt.Offset);
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraBoards.Select(board => board.CameraId));
        Assert.Equal(["B", "A", "C"], snapshot.GlobalBoard.Entries.Select(entry => entry.NumberPlate));
        Assert.Equal([1, 2, 3], snapshot.GlobalBoard.Entries.Select(entry => entry.Rank));
        Assert.All(snapshot.GlobalBoard.Entries, entry => Assert.Equal(3, entry.MeasurementCount));
        Assert.Equal(scenario.AtTime(0.3), snapshot.GlobalBoard.LatestEvent);

        var camera1 = snapshot.CameraBoards[0];
        Assert.Equal(["B", "A", "C"], camera1.Entries.Select(entry => entry.NumberPlate));
        Assert.Equal([160f, 150f, 140f], camera1.Entries.Select(entry => entry.SpeedKmh));
        Assert.Equal(scenario.AtTime(0.1), camera1.LatestEvent);
        Assert.All(snapshot.CameraBoards.Skip(3), board => Assert.Null(board.LatestEvent));

        var status = LivenessEvaluator.Evaluate(snapshot.GeneratedAt, snapshot.GlobalBoard.LatestEvent, TimeSpan.FromSeconds(3));
        Assert.Equal(Liveness.NotLive, status.Liveness);
    }

    [Fact]
    [Trait("BR", "BR-UI-03")]
    public async Task Unreachable_processor_is_reported_as_unavailable()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") };
        var client = new HttpSnapshotClient(http);

        var exception = await Assert.ThrowsAsync<SnapshotUnavailableException>(() => client.GetSnapshotAsync(Ct));

        Assert.StartsWith("processor unreachable", exception.Message, StringComparison.Ordinal);
    }
}
