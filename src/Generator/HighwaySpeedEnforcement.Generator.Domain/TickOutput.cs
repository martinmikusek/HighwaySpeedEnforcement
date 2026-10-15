namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// Everything one tick produced: the measurements in emission order (by camera, then by vehicle age), the cameras that send
/// a heartbeat, and the counters. <paramref name="Active"/> is the number of vehicles still on the road after the tick.
/// </summary>
public sealed record TickOutput(
    long TickNumber,
    long TimestampTicks,
    IReadOnlyList<Measurement> Measurements,
    IReadOnlyList<int> HeartbeatCameras,
    int Spawned,
    int Removed,
    int Active);
