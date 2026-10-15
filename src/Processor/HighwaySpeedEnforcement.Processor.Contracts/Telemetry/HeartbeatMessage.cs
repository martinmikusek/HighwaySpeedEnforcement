namespace HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

/// <summary>
/// Wire form of a heartbeat (BR-TEL-03, optional extension): a camera that sees no vehicle reports that nothing older than
/// <paramref name="TimestampTicks"/> is outstanding. Every property must be present.
/// </summary>
/// <param name="Type">Always <see cref="TelemetryContract.HeartbeatType"/>.</param>
/// <param name="CameraId">Camera 1 to 10.</param>
/// <param name="TimestampTicks">UTC <c>DateTime.Ticks</c> up to which the camera has delivered everything.</param>
public sealed record HeartbeatMessage(string Type, int CameraId, long TimestampTicks);
