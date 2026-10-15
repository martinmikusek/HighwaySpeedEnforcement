namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>The global leaderboard (BR-LB-02).</summary>
/// <param name="LatestEvent">The global watermark; null if no data was ever received.</param>
/// <param name="Entries">At most ten entries in rank order.</param>
public sealed record GlobalBoard(DateTimeOffset? LatestEvent, IReadOnlyList<GlobalEntry> Entries);
