namespace HighwaySpeedEnforcement.Processor.Contracts.Rest;

/// <summary>Body of <c>GET /api/traffic-snapshot</c> (BR-API-01, <c>docs/contracts/rest-api.md</c>). Serialized in camelCase.</summary>
/// <param name="GeneratedAtUtc">When the processor assembled the snapshot (processor clock).</param>
/// <param name="CameraBoards">Exactly 10 boards ordered by camera id 1 to 10, empty boards included.</param>
/// <param name="GlobalBoard">The global leaderboard.</param>
public sealed record TrafficSnapshotDto(DateTimeOffset GeneratedAtUtc, IReadOnlyList<CameraBoardDto> CameraBoards, GlobalBoardDto GlobalBoard);
