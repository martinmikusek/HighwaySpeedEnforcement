namespace HighwaySpeedEnforcement.Processor.Contracts.Rest;

/// <summary>One row of a camera leaderboard.</summary>
/// <param name="Rank">1-based, consecutive.</param>
/// <param name="NumberPlate">Vehicle identifier.</param>
/// <param name="SpeedKmh">Speed measured at this camera, unrounded.</param>
/// <param name="MeasuredAtUtc">Event time of the measurement.</param>
public sealed record CameraEntryDto(int Rank, string NumberPlate, float SpeedKmh, DateTimeOffset MeasuredAtUtc);
