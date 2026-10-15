namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>A decoded but not yet validated message handed to the <see cref="IIngestSink"/> by an input adapter.</summary>
public abstract record IncomingMessage;

/// <summary>Raw measurement values (BR-TEL-01); may be invalid, see <see cref="MeasurementValidator"/>.</summary>
public sealed record IncomingMeasurement(string? NumberPlate, long TimestampTicks, float CurrentSpeed, int CameraId) : IncomingMessage;

/// <summary>Raw heartbeat values (BR-TEL-03).</summary>
public sealed record IncomingHeartbeat(int CameraId, long TimestampTicks) : IncomingMessage;
