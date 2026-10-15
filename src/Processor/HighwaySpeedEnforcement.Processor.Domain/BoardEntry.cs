namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>
/// One self-contained candidate of a leaderboard (BR-LB-06). <paramref name="MeasurementCount"/> is the number of accepted measurements behind the score: always 1 on a camera entry, the vehicle count on a global entry (presented only there).
/// </summary>
public readonly record struct BoardEntry(string Plate, double Score, long TimestampTicks, long ExpiresAtTicks, int MeasurementCount);
