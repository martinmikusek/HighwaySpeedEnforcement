namespace HighwaySpeedEnforcement.Processor.Contracts.Rest;

/// <summary>The global leaderboard (BR-LB-02).</summary>
/// <param name="LatestEventUtc">Event time of the newest valid message (the global watermark); null if no data was ever received.</param>
/// <param name="Entries">At most 10 entries in rank order.</param>
public sealed record GlobalBoardDto(DateTimeOffset? LatestEventUtc, IReadOnlyList<GlobalEntryDto> Entries);
