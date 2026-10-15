namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>A validated measurement (BR-TEL-01). The domain's own type, not the wire type.</summary>
public readonly record struct Measurement(string Plate, long TimestampTicks, float Speed, int CameraId);
