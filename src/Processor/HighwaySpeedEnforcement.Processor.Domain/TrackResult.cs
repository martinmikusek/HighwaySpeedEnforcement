namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>
/// Outcome of <see cref="VehicleTracker.Process"/>: rejected as out of sequence (BR-ING-02), or accepted with the camera
/// entry to upsert, the global entry to upsert (null means no global action, BR-LB-03) and whether the vehicle completed.
/// </summary>
public readonly record struct TrackResult
{
    private TrackResult(bool isAccepted, BoardEntry cameraEntry, BoardEntry? globalEntry, bool completed)
    {
        IsAccepted = isAccepted;
        CameraEntry = cameraEntry;
        GlobalEntry = globalEntry;
        Completed = completed;
    }

    public static TrackResult OutOfSequence { get; } = new(false, default, null, false);

    public bool IsAccepted { get; }

    public BoardEntry CameraEntry { get; }

    public BoardEntry? GlobalEntry { get; }

    public bool Completed { get; }

    public static TrackResult Accepted(BoardEntry cameraEntry, BoardEntry? globalEntry, bool completed)
    {
        return new TrackResult(true, cameraEntry, globalEntry, completed);
    }
}
