namespace HighwaySpeedEnforcement.Processor.Contracts.Rest;

/// <summary>One camera leaderboard (BR-LB-01).</summary>
/// <param name="CameraId">Camera 1 to 10.</param>
/// <param name="LatestEventUtc">Event time of the newest message this board reflects (its watermark); null if it never received data.</param>
/// <param name="Entries">At most 10 entries in rank order.</param>
public sealed record CameraBoardDto(int CameraId, DateTimeOffset? LatestEventUtc, IReadOnlyList<CameraEntryDto> Entries);
