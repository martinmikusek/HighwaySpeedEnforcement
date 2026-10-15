using System.Net;
using System.Text.Json.Nodes;
using HighwaySpeedEnforcement.E2E.Tests.Support;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;

namespace HighwaySpeedEnforcement.E2E.Tests.Processor;

/// <summary>
/// Step 18 of the processor plan: real broker, real processor container, a test publisher instead of the generator and
/// an independent oracle. Window W = 2 s, linger L = 3 s, inactivity timeout 5 s unless a scenario says otherwise.
/// </summary>
[Collection(PipelineCollection.Name)]
[Trait("Category", "E2E")]
public sealed class ScriptedPipelineTests
{
    private readonly PipelineStack _stack;

    public ScriptedPipelineTests(PipelineStack stack)
    {
        _stack = stack;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("AC", "AC-22")]
    public async Task Snapshot_before_any_data_is_empty()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var snapshot = await scenario.SnapshotAsync(Ct);

        Assert.InRange(snapshot.GeneratedAtUtc, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraBoards.Select(board => board.CameraId));
        Assert.All(snapshot.CameraBoards, board => Assert.Empty(board.Entries));
        Assert.All(snapshot.CameraBoards, board => Assert.Null(board.LatestEventUtc));
        Assert.Empty(snapshot.GlobalBoard.Entries);
        Assert.Null(snapshot.GlobalBoard.LatestEventUtc);
        scenario.AssertAllBoards(snapshot);
    }

    [Fact]
    [Trait("AC", "AC-24")]
    public async Task Liveness_and_readiness_are_healthy_without_traffic()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);

        using var live = await scenario.Processor.GetAsync("health/live", Ct);
        using var ready = await scenario.Processor.GetAsync("health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var body = JsonNode.Parse(await ready.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("Ready", (string)body["status"]!);
        Assert.Equal("Connected", (string)body["broker"]!);
    }

    [Fact]
    [Trait("AC", "AC-10")]
    public async Task Vehicle_joins_the_global_board_with_its_third_measurement()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);

        await scenario.PublishAsync(Ct, new Telemetry("FAST", 1, scenario.At(0), 200f), new Telemetry("FAST", 2, scenario.At(1), 200f));
        var afterTwo = await scenario.SnapshotAsync(Ct);

        await scenario.PublishAsync(Ct, new Telemetry("FAST", 3, scenario.At(2), 200f));
        var afterThree = await scenario.SnapshotAsync(Ct);

        Assert.Empty(afterTwo.GlobalBoard.Entries);
        Assert.Equal("FAST", Assert.Single(afterThree.GlobalBoard.Entries).NumberPlate);
        scenario.AssertAllBoards(afterThree);
    }

    [Fact]
    [Trait("AC", "AC-11")]
    public async Task Lower_average_replaces_the_entry_and_a_completed_vehicle_within_the_linger_ranks_above()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var completed = Enumerable.Range(1, 10).Select(camera => new Telemetry("B", camera, scenario.At(camera * 0.1), 135f));
        var a = Enumerable.Range(1, 3).Select(camera => new Telemetry("A", camera, scenario.At(1 + camera * 0.1), 140f));

        await scenario.PublishAsync(completed.Concat(a), Ct);
        var before = await scenario.SnapshotAsync(Ct);
        await scenario.PublishAsync(Ct, new Telemetry("A", 4, scenario.At(1.5), 100f));
        var after = await scenario.SnapshotAsync(Ct);

        Assert.Equal(["A", "B"], before.GlobalBoard.Entries.Select(entry => entry.NumberPlate));
        Assert.Equal(["B", "A"], after.GlobalBoard.Entries.Select(entry => entry.NumberPlate));
        Assert.Equal(130.0, after.GlobalBoard.Entries[1].AverageSpeedKmh, 1e-9);
        Assert.Equal(4, after.GlobalBoard.Entries[1].MeasurementCount);
        scenario.AssertAllBoards(after);
    }

    [Fact]
    [Trait("AC", "AC-12")]
    public async Task Eleventh_vehicle_appears_when_one_above_it_falls()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var above = Enumerable.Range(0, 10).SelectMany(i => Enumerable.Range(1, 3).Select(camera => new Telemetry($"TOP{i}", camera, scenario.At(camera * 0.1), 150f + i)));
        var x = Enumerable.Range(1, 3).Select(camera => new Telemetry("X", camera, scenario.At(camera * 0.1 + 0.05), 140f));

        await scenario.PublishAsync(above.Concat(x), Ct);
        var before = await scenario.SnapshotAsync(Ct);
        await scenario.PublishAsync(Ct, new Telemetry("TOP0", 4, scenario.At(0.5), 0f));
        var after = await scenario.SnapshotAsync(Ct);

        Assert.DoesNotContain(before.GlobalBoard.Entries, entry => entry.NumberPlate == "X");
        Assert.Contains(after.GlobalBoard.Entries, entry => entry.NumberPlate == "X");
        Assert.DoesNotContain(after.GlobalBoard.Entries, entry => entry.NumberPlate == "TOP0");
        scenario.AssertGlobalBoard(after);
    }

    [Fact]
    [Trait("AC", "AC-15")]
    public async Task Equal_scores_are_ranked_by_plate_ordinally()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);

        await scenario.PublishAsync(
            Enumerable.Range(1, 3).SelectMany(camera => new[] { new Telemetry("AB1", camera, scenario.At(camera * 0.1), 140f), new Telemetry("AA9", camera, scenario.At(camera * 0.1), 140f) }),
            Ct);
        var snapshot = await scenario.SnapshotAsync(Ct);

        Assert.Equal(["AA9", "AB1"], snapshot.CameraBoards[0].Entries.Select(entry => entry.NumberPlate));
        Assert.Equal(["AA9", "AB1"], snapshot.GlobalBoard.Entries.Select(entry => entry.NumberPlate));
        scenario.AssertAllBoards(snapshot);
    }

    [Fact]
    [Trait("AC", "AC-16")]
    public async Task Camera_board_shows_the_ten_highest_speeds_of_a_tick_with_ties_by_plate()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var random = new Random(16);
        var tick = Enumerable.Range(0, 100).Select(i => new Telemetry($"T{i:D3}", 5, scenario.At(0), 120f + random.Next(0, 25)));

        await scenario.PublishAsync(tick, Ct);
        var snapshot = await scenario.SnapshotAsync(Ct);

        Assert.Equal(10, snapshot.CameraBoards[4].Entries.Count);
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraBoards[4].Entries.Select(entry => entry.Rank));
        scenario.AssertCameraBoard(snapshot, 5);
    }

    [Fact]
    [Trait("AC", "AC-17")]
    public async Task Camera_entry_expires_when_the_window_passes_and_the_next_best_takes_its_place()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var first = Enumerable.Range(0, 11).Select(i => new Telemetry($"OLD{i:D2}", 5, scenario.At(0), 150f - i));

        await scenario.PublishAsync(first, Ct);
        var before = await scenario.SnapshotAsync(Ct);
        await scenario.PublishAsync(Ct, new Telemetry("NEW", 5, scenario.At(2), 1f));
        var after = await scenario.SnapshotAsync(Ct);

        Assert.DoesNotContain(before.CameraBoards[4].Entries, entry => entry.NumberPlate == "OLD10");
        Assert.Equal(["NEW"], after.CameraBoards[4].Entries.Select(entry => entry.NumberPlate));
        scenario.AssertCameraBoard(after, 5);
    }

    [Fact]
    [Trait("AC", "AC-17")]
    public async Task Camera_entry_inside_the_window_stays_while_older_ones_leave()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);

        await scenario.PublishAsync(
            Ct,
            new Telemetry("AT0", 5, scenario.At(0), 150f),
            new Telemetry("AT1", 5, scenario.At(1), 140f),
            new Telemetry("AT2", 5, scenario.At(2), 130f));
        var snapshot = await scenario.SnapshotAsync(Ct);

        Assert.Equal(["AT1", "AT2"], snapshot.CameraBoards[4].Entries.Select(entry => entry.NumberPlate));
        scenario.AssertCameraBoard(snapshot, 5);
    }

    [Fact]
    [Trait("AC", "AC-18")]
    public async Task Boards_freeze_when_the_input_stops()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        await scenario.PublishAsync(
            Enumerable.Range(1, 4).SelectMany(camera => new[] { new Telemetry("F1", camera, scenario.At(camera), 150f), new Telemetry("F2", camera, scenario.At(camera), 140f) }),
            Ct);
        var first = await scenario.SnapshotAsync(Ct);

        // The scenario is about wall-clock time passing with no input: longer than the window, the linger and the timeout.
        await Task.Delay(ProcessorRun.Default.InactivityTimeout + TimeSpan.FromSeconds(1), Ct);
        var second = await scenario.SnapshotAsync(Ct);

        Assert.True(second.GeneratedAtUtc > first.GeneratedAtUtc);
        AssertSameBoards(first, second);
        Assert.Equal(scenario.AtTime(4), second.GlobalBoard.LatestEventUtc);
        Assert.NotEmpty(second.GlobalBoard.Entries);
        scenario.AssertAllBoards(second);
    }

    [Fact]
    [Trait("AC", "AC-19")]
    public async Task Camera_10_board_keeps_entries_of_completed_vehicles_within_the_window()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var passages = Enumerable.Range(0, 5).SelectMany(i => Enumerable.Range(1, 10).Select(camera => new Telemetry($"DONE{i}", camera, scenario.At(camera * 0.1 + i * 0.01), 130f + i)));

        await scenario.PublishAsync(passages, Ct);
        var snapshot = await scenario.SnapshotAsync(Ct);
        var status = await scenario.Processor.GetStatusAsync(Ct);

        Assert.Equal(["DONE4", "DONE3", "DONE2", "DONE1", "DONE0"], snapshot.CameraBoards[9].Entries.Select(entry => entry.NumberPlate));
        Assert.Equal(0, (int)status["ingest"]!["trackedVehicles"]!);
        scenario.AssertAllBoards(snapshot);
    }

    [Fact]
    [Trait("AC", "AC-34")]
    public async Task Dead_cameras_do_not_block_the_others_and_averages_use_accepted_measurements_only()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var withoutCamera7 = Enumerable.Range(1, 10).Where(camera => camera != 7).Select(camera => new Telemetry("NO7", camera, scenario.At(camera * 0.1), 100f + camera));
        var withoutCamera1 = Enumerable.Range(2, 9).Select(camera => new Telemetry("NO1", camera, scenario.At(camera * 0.1), 100f + camera));

        await scenario.PublishAsync(withoutCamera7.Concat(withoutCamera1), Ct);
        var snapshot = await scenario.SnapshotAsync(Ct);
        var status = await scenario.Processor.GetStatusAsync(Ct);

        Assert.Equal(0, (long)status["counters"]!["rejectedTotal"]!);
        Assert.Equal(0, (int)status["ingest"]!["trackedVehicles"]!);
        Assert.Equal(["NO7"], snapshot.CameraBoards[0].Entries.Select(entry => entry.NumberPlate));
        Assert.Equal(["NO1"], snapshot.CameraBoards[6].Entries.Select(entry => entry.NumberPlate));
        var no7 = Assert.Single(snapshot.GlobalBoard.Entries, entry => entry.NumberPlate == "NO7");
        var no1 = Assert.Single(snapshot.GlobalBoard.Entries, entry => entry.NumberPlate == "NO1");
        Assert.Equal(9, no7.MeasurementCount);
        Assert.Equal(9, no1.MeasurementCount);
        Assert.Equal(Enumerable.Range(1, 10).Where(c => c != 7).Average(c => 100.0 + c), no7.AverageSpeedKmh, 1e-9);
        Assert.Equal(Enumerable.Range(2, 9).Average(c => 100.0 + c), no1.AverageSpeedKmh, 1e-9);
        scenario.AssertAllBoards(snapshot);
    }

    [Fact]
    [Trait("AC", "AC-05")]
    public async Task Duplicate_delivery_is_rejected_out_of_sequence_without_changing_state()
    {
        await using var scenario = await Scenario.StartAsync(_stack, ProcessorRun.Default, Ct);
        var passage = Enumerable.Range(1, 3).Select(camera => new Telemetry("DUP", camera, scenario.At(camera * 0.1), 130f + camera)).ToArray();

        await scenario.PublishAsync(passage, Ct);
        var before = await scenario.SnapshotAsync(Ct);
        await scenario.PublishAsync(Ct, passage[2], passage[1]);
        var after = await scenario.SnapshotAsync(Ct);
        var status = await scenario.Processor.GetStatusAsync(Ct);

        Assert.Equal(2, (long)status["counters"]!["rejected"]!["OutOfSequence"]!);
        Assert.Equal(3, (long)status["counters"]!["accepted"]!);
        AssertSameBoards(before, after);
        Assert.Equal(2, scenario.Oracle.Rejected);
        scenario.AssertAllBoards(after);
    }

    /// <summary>Board DTOs hold lists, so records compare by reference; compare the rows and watermarks instead.</summary>
    private static void AssertSameBoards(TrafficSnapshotDto first, TrafficSnapshotDto second)
    {
        foreach (var (a, b) in first.CameraBoards.Zip(second.CameraBoards))
        {
            Assert.Equal(a.CameraId, b.CameraId);
            Assert.Equal(a.LatestEventUtc, b.LatestEventUtc);
            Assert.Equal(a.Entries, b.Entries);
        }

        Assert.Equal(first.GlobalBoard.LatestEventUtc, second.GlobalBoard.LatestEventUtc);
        Assert.Equal(first.GlobalBoard.Entries, second.GlobalBoard.Entries);
    }
}
