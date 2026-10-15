using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Domain;

public sealed class VehicleTrackerTests
{
    private const string PlateA = "A";

    private static readonly long Second = TimeSpan.FromSeconds(1).Ticks;

    private static readonly LivenessPolicy Policy = new(
        cameraBoardWindow: TimeSpan.FromSeconds(2),
        globalBoardLinger: TimeSpan.FromSeconds(3),
        inactivityTimeout: TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    [Trait("AC", "AC-06")]
    [Trait("BR", "BR-ING-02")]
    public void Process_accepts_a_plate_without_a_track_at_any_camera(int camera)
    {
        var tracker = new VehicleTracker(Policy);

        var result = tracker.Process(At(camera, seconds: 1, speed: 120));

        Assert.True(result.IsAccepted);
        Assert.Equal(new BoardEntry(PlateA, 120, 1 * Second, 3 * Second, MeasurementCount: 1), result.CameraEntry);
        Assert.Null(result.GlobalEntry);
    }

    [Fact]
    [Trait("AC", "AC-34")]
    [Trait("BR", "BR-ING-02")]
    [Trait("BR", "BR-ING-04")]
    public void Process_accepts_strictly_increasing_cameras_with_gaps_and_averages_the_accepted_speeds()
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(1, 1, 100));
        tracker.Process(At(2, 2, 110));

        var result = tracker.Process(At(5, 5, 150));

        Assert.True(result.IsAccepted);
        Assert.Equal(120, result.GlobalEntry!.Value.Score);
        Assert.Equal(3, result.GlobalEntry.Value.MeasurementCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    [Trait("AC", "AC-05")]
    [Trait("BR", "BR-ING-02")]
    [Trait("BR", "BR-ING-04")]
    public void Process_rejects_an_equal_or_lower_camera_and_leaves_the_track_unchanged(int repeatedCamera)
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(1, 1, 100));
        tracker.Process(At(2, 2, 110));
        tracker.Process(At(3, 3, 120));

        var rejected = tracker.Process(At(repeatedCamera, 4, 999));
        var next = tracker.Process(At(4, 4, 130));

        Assert.False(rejected.IsAccepted);
        Assert.Equal(TrackResult.OutOfSequence, rejected);
        Assert.Equal(115, next.GlobalEntry!.Value.Score);
        Assert.Equal(4, next.GlobalEntry.Value.MeasurementCount);
    }

    [Fact]
    [Trait("AC", "AC-06")]
    [Trait("BR", "BR-ING-02")]
    public void Process_rejects_a_lower_camera_after_a_late_start()
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(4, 4, 100));

        var result = tracker.Process(At(2, 5, 100));

        Assert.False(result.IsAccepted);
    }

    [Fact]
    [Trait("AC", "AC-07")]
    [Trait("BR", "BR-ING-05")]
    [Trait("BR", "BR-LB-03")]
    public void Process_completes_at_camera_10_and_a_repeated_camera_10_yields_no_global_action()
    {
        var tracker = new VehicleTracker(Policy);

        for (int camera = 1; camera < 10; camera++)
        {
            tracker.Process(At(camera, camera, 100));
        }

        var completed = tracker.Process(At(10, 10, 100));
        var repeated = tracker.Process(At(10, 10, 100));

        Assert.True(completed.Completed);
        Assert.Equal(0, tracker.Count);
        Assert.True(repeated.IsAccepted);
        Assert.True(repeated.Completed);
        Assert.Equal(completed.CameraEntry, repeated.CameraEntry);
        Assert.Null(repeated.GlobalEntry);
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    [Trait("AC", "AC-10")]
    [Trait("BR", "BR-LB-03")]
    public void Process_has_no_global_action_before_the_third_accepted_measurement()
    {
        var tracker = new VehicleTracker(Policy);

        var first = tracker.Process(At(1, 1, 250));
        var second = tracker.Process(At(2, 2, 250));
        var third = tracker.Process(At(3, 3, 250));

        Assert.Null(first.GlobalEntry);
        Assert.Null(second.GlobalEntry);
        Assert.Equal(new BoardEntry(PlateA, 250, 3 * Second, 8 * Second, MeasurementCount: 3), third.GlobalEntry);
    }

    [Fact]
    [Trait("AC", "AC-11")]
    [Trait("BR", "BR-LB-03")]
    public void Process_returns_an_upsert_with_the_lower_average_when_the_average_decreases()
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(1, 1, 140));
        tracker.Process(At(2, 2, 140));
        tracker.Process(At(3, 3, 140));

        var result = tracker.Process(At(4, 4, 100));

        Assert.Equal(130, result.GlobalEntry!.Value.Score);
    }

    [Fact]
    [Trait("AC", "AC-13")]
    [Trait("BR", "BR-LB-04")]
    [Trait("BR", "BR-LB-08")]
    public void Completed_global_entry_carries_the_final_average_and_the_camera_10_timestamp_and_expires_after_the_linger()
    {
        var tracker = new VehicleTracker(Policy);

        for (int camera = 1; camera < 10; camera++)
        {
            tracker.Process(At(camera, camera, 100 + camera));
        }

        var result = tracker.Process(At(10, 10, 110));

        Assert.Equal(new BoardEntry(PlateA, 105.5, 10 * Second, 13 * Second, MeasurementCount: 10), result.GlobalEntry);
    }

    [Fact]
    [Trait("BR", "BR-ING-06")]
    public void In_flight_global_entry_expires_after_the_inactivity_timeout()
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(1, 1, 100));
        tracker.Process(At(2, 2, 100));

        var result = tracker.Process(At(3, 3, 100));

        Assert.Equal(8 * Second, result.GlobalEntry!.Value.ExpiresAtTicks);
    }

    [Fact]
    [Trait("AC", "AC-08")]
    [Trait("BR", "BR-ING-06")]
    public void Process_starts_a_new_track_when_the_stamp_is_the_timeout_or_more_past_the_last_timestamp()
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(1, 1, 100));
        tracker.Process(At(2, 2, 100));
        tracker.Process(At(3, 3, 100));

        var result = tracker.Process(At(4, 8, 200, globalSeconds: 8));

        Assert.True(result.IsAccepted);
        Assert.Null(result.GlobalEntry);
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    [Trait("AC", "AC-08")]
    [Trait("BR", "BR-ING-06")]
    public void Process_keeps_the_track_while_the_silence_is_shorter_than_the_timeout()
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(1, 1, 100));
        tracker.Process(At(2, 2, 100));
        tracker.Process(At(3, 3, 100));

        var result = tracker.Process(At(4, 7, 100, globalSeconds: 7));

        Assert.Equal(4, result.GlobalEntry!.Value.MeasurementCount);
    }

    [Fact]
    [Trait("AC", "AC-08")]
    [Trait("BR", "BR-ING-06")]
    public void Sweep_frees_only_silent_tracks_and_does_not_change_later_results()
    {
        var swept = new VehicleTracker(Policy);
        var unswept = new VehicleTracker(Policy);

        foreach (var tracker in new[] { swept, unswept })
        {
            tracker.Process(At(1, 1, 100, plate: "Silent"));
            tracker.Process(At(1, 5, 100, plate: "Active"));
        }

        int removed = swept.Sweep(6 * Second);

        Assert.Equal(1, removed);
        Assert.Equal(1, swept.Count);
        Assert.Equal(2, unswept.Count);

        var afterSweep = swept.Process(At(2, 7, 100, plate: "Silent", globalSeconds: 7));
        var withoutSweep = unswept.Process(At(2, 7, 100, plate: "Silent", globalSeconds: 7));

        Assert.Equal(afterSweep, withoutSweep);
        Assert.Equal(swept.Process(At(2, 7, 100, plate: "Active", globalSeconds: 7)), unswept.Process(At(2, 7, 100, plate: "Active", globalSeconds: 7)));
    }

    /// <summary>AMB-10: a plate that reappears after completion is a new passage; its entry replaces the completed one.</summary>
    [Fact]
    [Trait("BR", "BR-LB-10")]
    [Trait("BR", "BR-ING-05")]
    public void Second_passage_after_completion_replaces_the_completed_global_entry()
    {
        var tracker = new VehicleTracker(Policy);
        var board = new RankedBoard();

        for (int camera = 1; camera <= 10; camera++)
        {
            Apply(board, tracker.Process(At(camera, camera, 150)));
        }

        for (int camera = 1; camera <= 3; camera++)
        {
            Apply(board, tracker.Process(At(camera, 10 + camera, 100, globalSeconds: 10 + camera)));
        }

        var entry = Assert.Single(board.Top(10).Entries).Entry;
        Assert.Equal(100, entry.Score);
        Assert.Equal(3, entry.MeasurementCount);
    }

    [Fact]
    [Trait("BR", "BR-ING-05")]
    public void Count_reports_the_in_flight_vehicles()
    {
        var tracker = new VehicleTracker(Policy);
        tracker.Process(At(1, 1, 100, plate: "A"));
        tracker.Process(At(1, 1, 100, plate: "B"));
        tracker.Process(At(10, 1, 100, plate: "C"));

        Assert.Equal(2, tracker.Count);
    }

    private static void Apply(RankedBoard globalBoard, TrackResult result)
    {
        if (result.GlobalEntry is { } entry)
        {
            globalBoard.Update(entry.TimestampTicks, entry);
        }
    }

    private static StampedMeasurement At(int camera, int seconds, float speed, string plate = PlateA, int? globalSeconds = null)
    {
        long timestamp = seconds * Second;

        return new StampedMeasurement(
            new Measurement(plate, timestamp, speed, camera),
            new Stamp(GlobalTicks: (globalSeconds ?? seconds) * Second, CameraTicks: timestamp));
    }
}
