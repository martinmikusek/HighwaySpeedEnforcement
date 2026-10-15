namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>One row of a camera leaderboard.</summary>
/// <param name="Rank">1-based, consecutive.</param>
/// <param name="NumberPlate">Vehicle identifier.</param>
/// <param name="SpeedKmh">Speed measured at the camera, unrounded.</param>
/// <param name="MeasuredAt">Event time of the measurement (UTC).</param>
public sealed record CameraEntry(int Rank, string NumberPlate, float SpeedKmh, DateTimeOffset MeasuredAt);
