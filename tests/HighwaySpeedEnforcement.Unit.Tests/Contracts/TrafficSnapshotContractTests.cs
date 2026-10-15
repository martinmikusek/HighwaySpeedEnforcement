using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;

namespace HighwaySpeedEnforcement.Unit.Tests.Contracts;

/// <summary>Contract tests of <c>docs/contracts/rest-api.md</c> against its approved example file.</summary>
public sealed partial class TrafficSnapshotContractTests
{
    private static readonly DateTimeOffset Watermark = new(2026, 10, 14, 14, 3, 9, 400, TimeSpan.Zero);

    [Fact]
    [Trait("BR", "BR-API-01")]
    public void Example_deserializes_into_the_dto()
    {
        var json = ContractExamples.Read("traffic-snapshot.json");

        var snapshot = JsonSerializer.Deserialize(json, RestJsonContext.Default.TrafficSnapshotDto);

        Assert.NotNull(snapshot);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 3, 10, TimeSpan.Zero), snapshot.GeneratedAtUtc);
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraBoards.Select(board => board.CameraId));

        var camera1 = snapshot.CameraBoards[0];
        Assert.Equal(Watermark, camera1.LatestEventUtc);
        Assert.Equal(
            [
                new CameraEntryDto(1, "4S7 1234", 152.4f, new DateTimeOffset(2026, 10, 14, 14, 3, 9, 398, TimeSpan.Zero)),
                new CameraEntryDto(2, "2AB 9876", 149.8f, Watermark),
            ],
            camera1.Entries);

        Assert.Empty(snapshot.CameraBoards[2].Entries);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 1, 2, TimeSpan.Zero), snapshot.CameraBoards[6].LatestEventUtc);
        Assert.Null(snapshot.CameraBoards[9].LatestEventUtc);
        Assert.Empty(snapshot.CameraBoards[9].Entries);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 3, 9, 700, TimeSpan.Zero), snapshot.GlobalBoard.LatestEventUtc);
        Assert.Equal(
            [
                new GlobalEntryDto(1, "4S7 1234", 144.35, 6, new DateTimeOffset(2026, 10, 14, 14, 3, 9, 398, TimeSpan.Zero)),
                new GlobalEntryDto(2, "2AB 9876", 143.9, 10, new DateTimeOffset(2026, 10, 14, 14, 3, 8, 500, TimeSpan.Zero)),
            ],
            snapshot.GlobalBoard.Entries);
    }

    [Fact]
    [Trait("BR", "BR-API-01")]
    public void Example_round_trips_to_equivalent_json()
    {
        var example = ContractExamples.Read("traffic-snapshot.json");
        var snapshot = JsonSerializer.Deserialize(example, RestJsonContext.Default.TrafficSnapshotDto);

        var json = JsonSerializer.Serialize(snapshot, RestJsonContext.Default.TrafficSnapshotDto);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(example), JsonNode.Parse(json)), json);
    }

    [Fact]
    [Trait("BR", "BR-API-01")]
    public void Serialized_snapshot_uses_camel_case_and_iso_8601_utc_text()
    {
        var snapshot = new TrafficSnapshotDto(
            new DateTimeOffset(2026, 10, 14, 16, 3, 10, TimeSpan.FromHours(2)),
            [new CameraBoardDto(1, null, [new CameraEntryDto(1, "X", 100f, Watermark)])],
            new GlobalBoardDto(null, []));

        var json = JsonSerializer.Serialize(snapshot, RestJsonContext.Default.TrafficSnapshotDto);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(["generatedAtUtc", "cameraBoards", "globalBoard"], node.Select(property => property.Key));
        Assert.Equal(["cameraId", "latestEventUtc", "entries"], node["cameraBoards"]![0]!.AsObject().Select(property => property.Key));
        Assert.Equal(["rank", "numberPlate", "speedKmh", "measuredAtUtc"], node["cameraBoards"]![0]!["entries"]![0]!.AsObject().Select(property => property.Key));
        Assert.Equal(["latestEventUtc", "entries"], node["globalBoard"]!.AsObject().Select(property => property.Key));

        Assert.Equal("2026-10-14T14:03:10.0000000Z", (string?)node["generatedAtUtc"]);
        Assert.Equal("2026-10-14T14:03:09.4000000Z", (string?)node["cameraBoards"]![0]!["entries"]![0]!["measuredAtUtc"]);
        Assert.Contains("\"latestEventUtc\":null", json, StringComparison.Ordinal);
        Assert.Null(node["globalBoard"]!["latestEventUtc"]);
        Assert.All(IsoUtcTimestamp().Matches(json).Select(match => match.Value), text => Assert.EndsWith("Z", text, StringComparison.Ordinal));
        Assert.Equal(2, IsoUtcTimestamp().Matches(json).Count);
    }

    [Fact]
    public void Times_with_an_offset_are_read_as_the_same_instant()
    {
        const string json = """{"generatedAtUtc":"2026-10-14T16:03:10+02:00","cameraBoards":[],"globalBoard":{"latestEventUtc":null,"entries":[]}}""";

        var snapshot = JsonSerializer.Deserialize(json, RestJsonContext.Default.TrafficSnapshotDto);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 3, 10, TimeSpan.Zero), snapshot!.GeneratedAtUtc);
        Assert.Equal(TimeSpan.Zero, snapshot.GeneratedAtUtc.Offset);
    }

    [Fact]
    public void Unknown_properties_are_ignored_by_the_client_side()
    {
        const string json = """{"generatedAtUtc":"2026-10-14T14:03:10Z","schemaVersion":2,"cameraBoards":[],"globalBoard":{"latestEventUtc":null,"entries":[],"note":"x"}}""";

        var snapshot = JsonSerializer.Deserialize(json, RestJsonContext.Default.TrafficSnapshotDto);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.GlobalBoard.Entries);
    }

    [GeneratedRegex("\\d{4}-\\d{2}-\\d{2}T[^\"]*")]
    private static partial Regex IsoUtcTimestamp();
}
