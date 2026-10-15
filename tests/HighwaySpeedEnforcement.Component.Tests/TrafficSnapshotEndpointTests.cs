using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;

namespace HighwaySpeedEnforcement.Component.Tests;

/// <summary>Step 16: <c>GET /api/traffic-snapshot</c> through the real host with the in-memory pipeline (BR-API-01, BR-API-02).</summary>
public sealed class TrafficSnapshotEndpointTests : IClassFixture<ProcessorFactory>
{
    private readonly ProcessorFactory _factory;

    public TrafficSnapshotEndpointTests(ProcessorFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("AC", "AC-22")]
    [Trait("BR", "BR-API-01")]
    public async Task Empty_snapshot_has_ten_empty_boards_null_latest_events_and_no_store()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();
        var before = DateTimeOffset.UtcNow;

        using var response = await client.GetAsync("/api/traffic-snapshot", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var json = await response.Content.ReadAsStringAsync(Ct);
        var snapshot = JsonSerializer.Deserialize(json, RestJsonContext.Default.TrafficSnapshotDto)!;
        Assert.InRange(snapshot.GeneratedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, snapshot.GeneratedAtUtc.Offset);
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraBoards.Select(board => board.CameraId));
        Assert.All(snapshot.CameraBoards, board => Assert.Empty(board.Entries));
        Assert.All(snapshot.CameraBoards, board => Assert.Null(board.LatestEventUtc));
        Assert.Empty(snapshot.GlobalBoard.Entries);
        Assert.Null(snapshot.GlobalBoard.LatestEventUtc);
        Assert.Contains("\"latestEventUtc\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("AC", "AC-10")]
    [Trait("AC", "AC-15")]
    [Trait("BR", "BR-API-01")]
    [Trait("BR", "BR-LB-05")]
    public async Task Entries_appear_with_ranks_order_and_event_times_after_ingestion()
    {
        var t0 = new DateTimeOffset(DateTimeOffset.UtcNow.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long At(int second) => (t0 + TimeSpan.FromSeconds(second)).UtcTicks;
        await _factory.IngestAsync(
            [
                new IncomingMeasurement("FAST 1", At(1), 150f, 1),
                new IncomingMeasurement("SLOW 1", At(1), 120f, 1),
                new IncomingMeasurement("TIE A", At(1), 130f, 1),
                new IncomingMeasurement("TIE B", At(1), 130f, 1),
                new IncomingMeasurement("FAST 1", At(2), 160f, 2),
                new IncomingMeasurement("FAST 1", At(3), 170f, 3),
                new IncomingMeasurement("SLOW 1", At(2), 120f, 2),
            ],
            Ct);
        using var client = _factory.CreateClient();

        var snapshot = await client.GetFromJsonAsync("/api/traffic-snapshot", RestJsonContext.Default.TrafficSnapshotDto, Ct);

        Assert.NotNull(snapshot);
        var camera1 = snapshot.CameraBoards[0];
        Assert.Equal(1, camera1.CameraId);
        Assert.Equal(t0 + TimeSpan.FromSeconds(1), camera1.LatestEventUtc);
        Assert.Equal(
            [
                new CameraEntryDto(1, "FAST 1", 150f, t0 + TimeSpan.FromSeconds(1)),
                new CameraEntryDto(2, "TIE A", 130f, t0 + TimeSpan.FromSeconds(1)),
                new CameraEntryDto(3, "TIE B", 130f, t0 + TimeSpan.FromSeconds(1)),
                new CameraEntryDto(4, "SLOW 1", 120f, t0 + TimeSpan.FromSeconds(1)),
            ],
            camera1.Entries);
        Assert.Equal(t0 + TimeSpan.FromSeconds(3), snapshot.CameraBoards[2].LatestEventUtc);
        Assert.Null(snapshot.CameraBoards[3].LatestEventUtc);

        Assert.Equal(t0 + TimeSpan.FromSeconds(3), snapshot.GlobalBoard.LatestEventUtc);
        var global = Assert.Single(snapshot.GlobalBoard.Entries);
        Assert.Equal(new GlobalEntryDto(1, "FAST 1", 160.0, 3, t0 + TimeSpan.FromSeconds(3)), global);
    }

    [Fact]
    [Trait("BR", "BR-API-01")]
    public async Task Json_has_the_shape_of_the_contract_example()
    {
        using var factory = new ProcessorFactory();
        var now = DateTimeOffset.UtcNow.UtcTicks;
        await factory.IngestAsync(
            Enumerable.Range(1, 3).Select(camera => new IncomingMeasurement("SHAPE", now + camera, 130f, camera)),
            Ct);
        using var client = factory.CreateClient();

        var actual = JsonNode.Parse(await client.GetStringAsync("/api/traffic-snapshot", Ct))!.AsObject();
        var example = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "traffic-snapshot.json")))!.AsObject();

        Assert.Equal(Keys(example), Keys(actual));
        Assert.Equal(Keys(example["cameraBoards"]![0]!), Keys(actual["cameraBoards"]![0]!));
        Assert.Equal(Keys(example["cameraBoards"]![0]!["entries"]![0]!), Keys(actual["cameraBoards"]![0]!["entries"]![0]!));
        Assert.Equal(Keys(example["globalBoard"]!), Keys(actual["globalBoard"]!));
        Assert.Equal(Keys(example["globalBoard"]!["entries"]![0]!), Keys(actual["globalBoard"]!["entries"]![0]!));
        Assert.EndsWith("Z", (string)actual["generatedAtUtc"]!, StringComparison.Ordinal);
        Assert.EndsWith("Z", (string)actual["globalBoard"]!["latestEventUtc"]!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("AC", "AC-23")]
    [Trait("BR", "BR-API-02")]
    public async Task Concurrent_requests_during_ingestion_return_valid_consistently_ordered_boards()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var random = new Random(23);
        var traffic = Enumerable.Range(0, 300)
            .SelectMany(vehicle => Enumerable.Range(1, 10).Select(camera => new IncomingMeasurement($"V{vehicle:D4}", now + vehicle + camera, 100f + random.Next(0, 60), camera)))
            .ToArray();
        var ingestion = Task.Run(() => factory.IngestAsync(traffic, Ct), Ct);

        var snapshots = new List<TrafficSnapshotDto>();
        while (!ingestion.IsCompleted)
        {
            var batch = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetFromJsonAsync("/api/traffic-snapshot", RestJsonContext.Default.TrafficSnapshotDto, Ct)));
            snapshots.AddRange(batch!);
        }

        await ingestion;
        snapshots.Add((await client.GetFromJsonAsync("/api/traffic-snapshot", RestJsonContext.Default.TrafficSnapshotDto, Ct))!);

        Assert.All(snapshots, AssertValid);
        var final = snapshots[^1];
        Assert.Equal(10, final.GlobalBoard.Entries.Count);
        Assert.All(final.CameraBoards, board => Assert.Equal(10, board.Entries.Count));
    }

    [Fact]
    [Trait("BR", "BR-API-03")]
    public async Task Snapshot_is_unavailable_while_the_pipeline_is_not_running()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();
        await factory.Pipeline.StopAsync(Ct);

        using var response = await client.GetAsync("/api/traffic-snapshot", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static void AssertValid(TrafficSnapshotDto snapshot)
    {
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraBoards.Select(board => board.CameraId));

        foreach (var board in snapshot.CameraBoards)
        {
            Assert.InRange(board.Entries.Count, 0, 10);
            Assert.Equal(Enumerable.Range(1, board.Entries.Count), board.Entries.Select(entry => entry.Rank));
            AssertRankOrder(board.Entries.Select(entry => ((double)entry.SpeedKmh, entry.NumberPlate)));
        }

        Assert.InRange(snapshot.GlobalBoard.Entries.Count, 0, 10);
        Assert.Equal(Enumerable.Range(1, snapshot.GlobalBoard.Entries.Count), snapshot.GlobalBoard.Entries.Select(entry => entry.Rank));
        AssertRankOrder(snapshot.GlobalBoard.Entries.Select(entry => (entry.AverageSpeedKmh, entry.NumberPlate)));
        Assert.All(snapshot.GlobalBoard.Entries, entry => Assert.InRange(entry.MeasurementCount, 3, 10));
    }

    /// <summary>BR-LB-05: score descending, then plate ascending ordinal.</summary>
    private static void AssertRankOrder(IEnumerable<(double Score, string Plate)> entries)
    {
        var list = entries.ToList();

        for (int i = 1; i < list.Count; i++)
        {
            var (previous, current) = (list[i - 1], list[i]);
            Assert.True(
                previous.Score > current.Score || (previous.Score == current.Score && string.CompareOrdinal(previous.Plate, current.Plate) < 0),
                $"{previous} must rank before {current}");
        }
    }

    private static IEnumerable<string> Keys(JsonNode node)
    {
        return node.AsObject().Select(property => property.Key);
    }
}
