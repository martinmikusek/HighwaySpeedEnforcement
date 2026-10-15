namespace HighwaySpeedEnforcement.Processor.Contracts.Rest;

/// <summary>One row of the global leaderboard.</summary>
/// <param name="Rank">1-based, consecutive.</param>
/// <param name="NumberPlate">Vehicle identifier.</param>
/// <param name="AverageSpeedKmh">Cumulative average over the accepted measurements, unrounded (BR-ING-04).</param>
/// <param name="MeasurementCount">How many measurements the average is based on (3 to 10).</param>
/// <param name="MeasuredAtUtc">Event time of the vehicle's latest accepted measurement.</param>
public sealed record GlobalEntryDto(int Rank, string NumberPlate, double AverageSpeedKmh, int MeasurementCount, DateTimeOffset MeasuredAtUtc);
