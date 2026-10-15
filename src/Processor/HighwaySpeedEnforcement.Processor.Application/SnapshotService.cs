using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// Assembles the traffic snapshot: each board's top 10 is read under that board's lock, so the consistency boundary is one
/// board; the boards are not read at a single instant (BR-API-02).
/// </summary>
public sealed class SnapshotService
{
    private readonly BoardSet _boards;
    private readonly TimeProvider _timeProvider;

    public SnapshotService(BoardSet boards, TimeProvider timeProvider)
    {
        _boards = boards;
        _timeProvider = timeProvider;
    }

    public TrafficSnapshot Take()
    {
        var cameras = new BoardView[HighwayLayout.CameraCount];

        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i] = _boards.Cameras[i].Top(HighwayLayout.BoardSize);
        }

        var global = _boards.Global.Top(HighwayLayout.BoardSize);

        return new TrafficSnapshot(_timeProvider.GetUtcNow(), cameras, global);
    }
}
