namespace HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

/// <summary>
/// Wire form of a measurement (BR-TEL-01). Property names are the assignment's, PascalCase, and every property must be present.
/// Values are not validated here: <paramref name="NumberPlate"/> may be null on the wire, which validation rejects as <c>InvalidPlate</c> (BR-ING-01).
/// </summary>
/// <param name="Type">Always <see cref="TelemetryContract.MeasurementType"/>.</param>
/// <param name="NumberPlate">Vehicle identifier, 1 to 16 characters.</param>
/// <param name="TimestampTicks">UTC <c>DateTime.Ticks</c> at measurement time (BR-TEL-02).</param>
/// <param name="CurrentSpeed">Speed in km/h at the camera.</param>
/// <param name="CameraId">Camera 1 to 10.</param>
public sealed record MeasurementMessage(string Type, string? NumberPlate, long TimestampTicks, float CurrentSpeed, int CameraId);
