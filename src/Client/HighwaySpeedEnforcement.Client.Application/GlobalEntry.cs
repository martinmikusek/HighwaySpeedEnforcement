namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>One row of the global leaderboard.</summary>
/// <param name="Rank">1-based, consecutive.</param>
/// <param name="NumberPlate">Vehicle identifier.</param>
/// <param name="AverageSpeedKmh">Cumulative average, unrounded.</param>
/// <param name="MeasurementCount">How many measurements the average is based on.</param>
/// <param name="MeasuredAt">Event time of the vehicle's latest accepted measurement (UTC).</param>
public sealed record GlobalEntry(int Rank, string NumberPlate, double AverageSpeedKmh, int MeasurementCount, DateTimeOffset MeasuredAt);
