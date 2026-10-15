namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>One camera leaderboard (BR-LB-01).</summary>
/// <param name="CameraId">Camera 1 to 10.</param>
/// <param name="LatestEvent">The board's watermark; null if it never received data.</param>
/// <param name="Entries">At most ten entries in rank order.</param>
public sealed record CameraBoard(int CameraId, DateTimeOffset? LatestEvent, IReadOnlyList<CameraEntry> Entries);
