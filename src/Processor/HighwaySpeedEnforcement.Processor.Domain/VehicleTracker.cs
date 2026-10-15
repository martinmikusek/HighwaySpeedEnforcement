namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>
/// Owns the vehicle tracks of one partition (no locking: one worker owns a tracker). Applies the sequence rule (BR-ING-02),
/// the orphan rule (BR-ING-06), completion (BR-ING-05) and the global board gate (BR-LB-03), and returns board actions.
/// </summary>
public sealed class VehicleTracker
{
    private readonly Dictionary<string, VehicleTrack> _tracks = new(StringComparer.Ordinal);
    private readonly LivenessPolicy _policy;

    public VehicleTracker(LivenessPolicy policy)
    {
        _policy = policy;
    }

    /// <summary>Number of in-flight vehicles tracked by this tracker.</summary>
    public int Count => _tracks.Count;

    public TrackResult Process(StampedMeasurement stamped)
    {
        var measurement = stamped.Measurement;
        _tracks.TryGetValue(measurement.Plate, out var track);

        // BR-ING-06: the watermark stamped on this message decides whether the track is still valid, whether or not Sweep ran.
        if (track is not null && IsOrphaned(track, stamped.Stamp.GlobalTicks))
        {
            _tracks.Remove(measurement.Plate);
            track = null;
        }

        if (track is not null && measurement.CameraId <= track.LastCamera)
        {
            return TrackResult.OutOfSequence;
        }

        if (track is null)
        {
            track = new VehicleTrack(measurement.Plate);
            _tracks.Add(measurement.Plate, track);
        }

        track.Add(measurement);

        var cameraEntry = new BoardEntry(
            measurement.Plate,
            measurement.Speed,
            measurement.TimestampTicks,
            _policy.CameraExpiry(measurement.TimestampTicks),
            MeasurementCount: 1);

        bool completed = measurement.CameraId == HighwayLayout.LastCamera;

        if (completed)
        {
            _tracks.Remove(measurement.Plate);
        }

        BoardEntry? globalEntry = track.Count >= HighwayLayout.MinimumMeasurementsForGlobalBoard
            ? new BoardEntry(
                measurement.Plate,
                track.Average,
                track.LastTimestampTicks,
                completed ? _policy.CompletedExpiry(track.LastTimestampTicks) : _policy.InFlightExpiry(track.LastTimestampTicks),
                track.Count)
            : null;

        return TrackResult.Accepted(cameraEntry, globalEntry, completed);
    }

    /// <summary>Frees the tracks of orphaned vehicles. Only memory: results of later messages do not depend on when it runs.</summary>
    public int Sweep(long globalWatermarkTicks)
    {
        int removed = 0;

        foreach (var (plate, track) in _tracks)
        {
            if (IsOrphaned(track, globalWatermarkTicks))
            {
                _tracks.Remove(plate);
                removed++;
            }
        }

        return removed;
    }

    private bool IsOrphaned(VehicleTrack track, long globalWatermarkTicks)
    {
        return globalWatermarkTicks - track.LastTimestampTicks >= _policy.InactivityTimeout.Ticks;
    }
}
