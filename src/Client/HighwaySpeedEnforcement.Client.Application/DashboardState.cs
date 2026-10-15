namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>
/// What the dashboard shows after a poll: the last successful snapshot with the liveness of its boards (BR-UI-04), and
/// whether the last poll failed (BR-UI-03). Immutable; the poller publishes a new instance after every poll.
/// </summary>
/// <param name="Snapshot">The last successful snapshot; null before the first success.</param>
/// <param name="GlobalStatus">Liveness of the global board of <paramref name="Snapshot"/>.</param>
/// <param name="CameraStatuses">Liveness of the ten camera boards, index 0 is camera 1.</param>
/// <param name="IsStale">True when the last poll failed, so the snapshot is older than one polling cycle.</param>
/// <param name="LastSuccessAt">Client clock time of the last successful poll; null before the first success.</param>
/// <param name="LastError">Reason of the last failed poll; null after a success.</param>
public sealed record DashboardState(
    TrafficSnapshot? Snapshot,
    BoardStatus GlobalStatus,
    IReadOnlyList<BoardStatus> CameraStatuses,
    bool IsStale,
    DateTimeOffset? LastSuccessAt,
    string? LastError)
{
    public static DashboardState Initial { get; } = new(
        null,
        BoardStatus.NoData,
        Enumerable.Repeat(BoardStatus.NoData, TrafficSnapshot.CameraCount).ToArray(),
        false,
        null,
        null);

    public bool HasData => Snapshot is not null;
}
