using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Domain;
using Microsoft.Extensions.Logging;
using static HighwaySpeedEnforcement.Unit.Tests.Processor.Application.PipelineHarness;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Application;

public sealed class IngestPipelineTests
{
    [Fact]
    [Trait("BR", "BR-ING-04")]
    [Trait("BR", "BR-LB-01")]
    [Trait("BR", "BR-LB-03")]
    public async Task Accepted_measurements_reach_the_camera_boards_and_the_global_board()
    {
        await using var harness = new PipelineHarness();

        await harness.SubmitAndDrainAsync(
            Measurement("A", 1, 1, 120),
            Measurement("A", 2, 2, 130),
            Measurement("A", 3, 3, 140));

        Assert.Equal(["A"], harness.CameraPlates(1));
        Assert.Equal(["A"], harness.CameraPlates(3));
        var global = Assert.Single(harness.Boards.Global.Top(10).Entries).Entry;
        Assert.Equal(130, global.Score);
        Assert.Equal(3, global.MeasurementCount);
        Assert.Equal(3, harness.Metrics.AcceptedCount);
        Assert.Equal(At(3), harness.Boards.Global.LatestEventTicks);
    }

    [Fact]
    [Trait("AC", "AC-09")]
    [Trait("BR", "BR-ING-03")]
    public async Task Messages_of_one_plate_are_applied_in_submission_order()
    {
        await using var harness = new PipelineHarness();

        await harness.SubmitAndDrainAsync(
            Measurement("A", 1, 1, 100),
            Measurement("A", 2, 2, 100),
            Measurement("A", 2, 3, 999),
            Measurement("A", 3, 3, 100));

        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.OutOfSequence));
        Assert.Equal(100, Assert.Single(harness.Boards.Global.Top(10).Entries).Entry.Score);
    }

    /// <summary>
    /// The submission order is fixed: stamping happens in arrival order by design (ADR-0003), so the orphan rule may decide
    /// differently if plates are reordered by more than the inactivity timeout. Only the partition count and the worker
    /// interleaving must not matter (BR-LB-13).
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [Trait("AC", "AC-30")]
    [Trait("BR", "BR-LB-13")]
    public async Task Boards_are_identical_for_every_partition_count(int partitionCount)
    {
        var expected = await RunAsync(new ProcessorSettings { PartitionCount = 1 }, Traffic());

        for (int run = 1; run <= 3; run++)
        {
            var actual = await RunAsync(new ProcessorSettings { PartitionCount = partitionCount }, Traffic());

            Assert.True(expected.SequenceEqual(actual), $"boards differ for partition count {partitionCount}, run {run}");
        }
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2, 3 })]
    [InlineData(new[] { 3, 2, 1, 0 })]
    [InlineData(new[] { 2, 0, 3, 1 })]
    [Trait("AC", "AC-30")]
    [Trait("BR", "BR-LB-13")]
    public async Task Boards_are_identical_when_the_workers_run_in_any_order(int[] workerOrder)
    {
        var expected = await RunAsync(new ProcessorSettings { PartitionCount = 1 }, Traffic());
        var messages = Traffic();
        var settings = new ProcessorSettings { PartitionCount = 4 };
        var turns = Enumerable.Range(0, 4).Select(_ => new SemaphoreSlim(0)).ToArray();
        var done = Enumerable.Range(0, 4).Select(_ => new SemaphoreSlim(0)).ToArray();

        await using var harness = new PipelineHarness(settings);
        harness.Pipeline.BeforeApply = partition => turns[partition].WaitAsync(TestContext.Current.CancellationToken);
        harness.Pipeline.AfterApply = partition => done[partition].Release();
        await harness.SubmitAsync(messages);

        // Workers apply strictly one after the other in the given partition order; each item is granted and awaited.
        foreach (int partition in workerOrder)
        {
            int items = messages.Count(m => PlateHash.Partition(m.NumberPlate!, 4) == partition);

            for (int i = 0; i < items; i++)
            {
                turns[partition].Release();
                await done[partition].WaitAsync(TestContext.Current.CancellationToken);
            }
        }

        await harness.Pipeline.WaitUntilIdleAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(expected.SequenceEqual(AllBoards(harness.Boards)), $"boards differ for worker order {string.Join(",", workerOrder)}");
    }

    [Fact]
    [Trait("AC", "AC-04")]
    [Trait("AC", "AC-29")]
    [Trait("BR", "BR-ING-07")]
    public async Task Rejected_messages_are_counted_by_reason_and_change_nothing()
    {
        await using var harness = new PipelineHarness();
        await harness.SubmitAndDrainAsync(Measurement("A", 5, 10));

        await harness.SubmitAndDrainAsync(
            Measurement("", 5, 11),
            Measurement("B", 0, 11),
            Measurement("B", 5, 11, -1),
            Measurement("B", 5, 11) with { TimestampTicks = 0 },
            Measurement("A", 5, 11));
        harness.Pipeline.ReportUndecodable(RejectReason.Malformed);

        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.InvalidPlate));
        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.InvalidCamera));
        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.InvalidSpeed));
        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.InvalidTimestamp));
        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.OutOfSequence));
        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.Malformed));
        Assert.Equal(7, harness.Metrics.ReceivedCount);
        Assert.Equal(1, harness.Metrics.AcceptedCount);
        Assert.Equal(["A"], harness.CameraPlates(5));
        Assert.Equal(1, harness.Pipeline.Status.TrackedVehicles);
    }

    [Fact]
    [Trait("AC", "AC-21")]
    [Trait("BR", "BR-LB-11")]
    public async Task Valid_but_out_of_sequence_measurement_advances_the_watermark_and_an_invalid_one_does_not()
    {
        await using var harness = new PipelineHarness();
        await harness.SubmitAndDrainAsync(Measurement("A", 5, 10));

        await harness.SubmitAndDrainAsync(Measurement("", 5, 50));
        Assert.Equal(At(10), harness.Boards.Camera(5).LatestEventTicks);

        await harness.SubmitAndDrainAsync(Measurement("A", 5, 13));
        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.OutOfSequence));
        Assert.Equal(At(13), harness.Boards.Camera(5).LatestEventTicks);
        Assert.Empty(harness.CameraPlates(5));
    }

    [Fact]
    [Trait("AC", "AC-29")]
    [Trait("BR", "BR-OPS-01")]
    public async Task Rejections_are_logged_once_per_reason_per_minute_at_warning_and_otherwise_at_debug()
    {
        await using var harness = new PipelineHarness(recordLogs: true);

        await harness.SubmitAndDrainAsync(Measurement("", 5, 1), Measurement("", 5, 2));
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.SubmitAndDrainAsync(Measurement("", 5, 3));

        var rejections = harness.Logger!.Records.Where(r => r.Message.Contains("rejected", StringComparison.Ordinal)).Select(r => r.Level);
        Assert.Equal([LogLevel.Warning, LogLevel.Debug, LogLevel.Warning], rejections);
    }

    [Fact]
    [Trait("BR", "BR-ING-03")]
    public async Task SubmitAsync_waits_while_the_partition_channel_is_full_and_continues_when_the_worker_makes_room()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 1, IngestChannelCapacity = 1 });
        var gate = new SemaphoreSlim(0);
        harness.Pipeline.BeforeApply = _ => gate.WaitAsync(TestContext.Current.CancellationToken);

        await harness.SubmitAsync(Measurement("A", 1, 1));
        await harness.SubmitAsync(Measurement("B", 1, 1));
        var blocked = harness.Pipeline.SubmitAsync(Measurement("C", 1, 1), TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();

        Assert.False(blocked.IsCompleted);
        Assert.Equal(1, harness.Pipeline.Status.BufferDepth);

        gate.Release();
        await blocked.WaitAsync(TestContext.Current.CancellationToken);

        gate.Release(2);
        await harness.Pipeline.WaitUntilIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, harness.Metrics.AcceptedCount);
    }

    [Fact]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-TEL-03")]
    public async Task Heartbeat_advances_the_watermarks_and_expires_entries_without_touching_vehicle_state()
    {
        await using var harness = new PipelineHarness();
        await harness.SubmitAndDrainAsync(Measurement("A", 5, 10));

        await harness.SubmitAndDrainAsync(new IncomingHeartbeat(5, At(12)));

        Assert.Empty(harness.CameraPlates(5));
        Assert.Equal(At(12), harness.Boards.Camera(5).LatestEventTicks);
        Assert.Equal(At(12), harness.Boards.Global.LatestEventTicks);
        Assert.Equal(1, harness.Pipeline.Status.TrackedVehicles);
        Assert.Equal(1, harness.Metrics.AcceptedCount);
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public async Task Disabled_heartbeats_are_counted_as_ignored_and_change_nothing()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { HeartbeatsEnabled = false });
        await harness.SubmitAndDrainAsync(Measurement("A", 5, 10));

        await harness.SubmitAndDrainAsync(new IncomingHeartbeat(5, At(12)));

        Assert.Equal(1, harness.Metrics.IgnoredCount);
        Assert.Equal(At(10), harness.Boards.Camera(5).LatestEventTicks);
        Assert.Equal(["A"], harness.CameraPlates(5));
    }

    [Fact]
    [Trait("AC", "AC-08")]
    [Trait("BR", "BR-ING-06")]
    public async Task Sweep_frees_orphaned_tracks_as_event_time_advances()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 1 });

        await harness.SubmitAndDrainAsync(Measurement("Silent", 1, 0), Measurement("Other", 1, 3));
        Assert.Equal(2, harness.Pipeline.Status.TrackedVehicles);

        await harness.SubmitAndDrainAsync(Measurement("Other", 2, 5));
        Assert.Equal(1, harness.Pipeline.Status.TrackedVehicles);

        await harness.SubmitAndDrainAsync(Measurement("Silent", 2, 6));
        Assert.Equal(2, harness.Pipeline.Status.TrackedVehicles);
        Assert.Equal(4, harness.Metrics.AcceptedCount);
    }

    [Fact]
    [Trait("AC", "AC-31")]
    [Trait("BR", "BR-ING-01")]
    public async Task Far_future_timestamp_is_rejected_and_expires_nothing()
    {
        await using var harness = new PipelineHarness();
        await harness.SubmitAndDrainAsync(Measurement("A", 5, 10), Measurement("A", 6, 11), Measurement("A", 7, 12));

        await harness.SubmitAndDrainAsync(new IncomingMeasurement("B", Now.UtcTicks + TimeSpan.FromMinutes(2).Ticks, 100, 5));

        Assert.Equal(1, harness.Metrics.RejectedCount(RejectReason.InvalidTimestamp));
        Assert.Equal(At(10), harness.Boards.Camera(5).LatestEventTicks);
        Assert.Equal(["A"], harness.CameraPlates(5));
        Assert.Equal(["A"], harness.GlobalPlates());
    }

    [Fact]
    [Trait("BR", "BR-ING-03")]
    public async Task StopAsync_drains_the_queued_items()
    {
        var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 2 });
        var gate = new SemaphoreSlim(0);
        harness.Pipeline.BeforeApply = _ => gate.WaitAsync(TestContext.Current.CancellationToken);
        var messages = Enumerable.Range(0, 50).Select(i => Measurement($"P{i}", 1, 1)).ToArray();
        await harness.SubmitAsync(messages);

        var stopping = harness.Pipeline.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(harness.Pipeline.IsRunning);
        gate.Release(messages.Length);
        await stopping;

        Assert.Equal(messages.Length, harness.Metrics.AcceptedCount);
        Assert.Equal(0, harness.Pipeline.Status.BufferDepth);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Pipeline.SubmitAsync(messages[0], TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task Status_reports_tracked_vehicles_buffer_depth_newest_event_and_lag()
    {
        await using var harness = new PipelineHarness(new ProcessorSettings { PartitionCount = 1 });
        var idle = harness.Pipeline.Status;
        Assert.True(idle.IsRunning);
        Assert.Null(idle.NewestProcessedEvent);
        Assert.Null(idle.IngestLag);
        Assert.Null(idle.LastReceivedAt);

        await harness.SubmitAndDrainAsync(Measurement("A", 1, 0), Measurement("B", 1, 3));
        var gate = new SemaphoreSlim(0);
        var workerHoldsAnItem = new TaskCompletionSource();
        harness.Pipeline.BeforeApply = _ =>
        {
            workerHoldsAnItem.TrySetResult();
            return gate.WaitAsync(TestContext.Current.CancellationToken);
        };
        await harness.SubmitAsync(Measurement("C", 1, 4), Measurement("D", 1, 4));
        // The worker has taken "C" out of the channel and is parked in the hook; only "D" is buffered.
        await workerHoldsAnItem.Task;

        var status = harness.Pipeline.Status;
        Assert.Equal(2, status.TrackedVehicles);
        Assert.Equal(1, status.BufferDepth);
        Assert.Equal(new DateTimeOffset(At(3), TimeSpan.Zero), status.NewestProcessedEvent);
        Assert.Equal(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(3), status.IngestLag);
        Assert.Equal(Now, status.LastReceivedAt);

        gate.Release(2);
    }

    /// <summary>40 plates with 10 cameras each, interleaved in a fixed pseudo-random order that keeps each plate's own order.</summary>
    private static IncomingMeasurement[] Traffic()
    {
        const int plates = 40;
        var random = new Random(0);
        var perPlate = Enumerable.Range(0, plates)
            .Select(p => Enumerable.Range(1, 10)
                .Select(camera => Measurement($"P{p:00}", camera, p * 0.1 + camera, 100 + random.Next(60)))
                .ToList())
            .ToArray();
        var merged = new List<IncomingMeasurement>();

        while (merged.Count < plates * 10)
        {
            int p = random.Next(plates);

            if (perPlate[p].Count > 0)
            {
                merged.Add(perPlate[p][0]);
                perPlate[p].RemoveAt(0);
            }
        }

        return [.. merged];
    }

    private static async Task<List<RankedEntry>> RunAsync(ProcessorSettings settings, IncomingMeasurement[] messages)
    {
        await using var harness = new PipelineHarness(settings);
        await harness.SubmitAndDrainAsync(messages);

        return AllBoards(harness.Boards);
    }

    private static List<RankedEntry> AllBoards(BoardSet boards)
    {
        return boards.Cameras.Append(boards.Global).SelectMany(board => board.Top(int.MaxValue).Entries).ToList();
    }
}
