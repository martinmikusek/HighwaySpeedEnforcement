namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>The client's own model of one traffic snapshot (BR-API-01); the wire DTOs are mapped to it in Infrastructure.</summary>
/// <param name="GeneratedAt">When the processor assembled the snapshot (processor clock, UTC).</param>
/// <param name="CameraBoards">Exactly ten boards ordered by camera id 1 to 10.</param>
/// <param name="GlobalBoard">The global leaderboard.</param>
public sealed record TrafficSnapshot(DateTimeOffset GeneratedAt, IReadOnlyList<CameraBoard> CameraBoards, GlobalBoard GlobalBoard)
{
    public const int CameraCount = 10;
}
