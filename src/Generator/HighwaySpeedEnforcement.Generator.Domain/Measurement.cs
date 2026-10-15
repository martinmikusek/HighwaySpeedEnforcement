namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>One measurement of a vehicle at a camera (BR-TEL-01); the generator's own record, encoded on the wire by Infrastructure.</summary>
public readonly record struct Measurement(string NumberPlate, long TimestampTicks, float CurrentSpeed, int CameraId);
