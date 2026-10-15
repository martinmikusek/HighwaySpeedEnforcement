using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>The ten camera leaderboards and the global leaderboard (BR-LB-01, BR-LB-02), each with its own lock.</summary>
public sealed class BoardSet
{
    private readonly RankedBoard[] _cameras;

    public BoardSet(LivenessPolicy policy)
    {
        Policy = policy;
        _cameras = Enumerable.Range(0, HighwayLayout.CameraCount).Select(_ => new RankedBoard()).ToArray();
        Global = new RankedBoard();
    }

    public LivenessPolicy Policy { get; }

    public RankedBoard Global { get; }

    /// <summary>Camera boards in camera order (index 0 is Camera 1).</summary>
    public IReadOnlyList<RankedBoard> Cameras => _cameras;

    public RankedBoard Camera(int cameraId)
    {
        if (!HighwayLayout.IsCamera(cameraId))
        {
            throw new ArgumentOutOfRangeException(nameof(cameraId), cameraId, "Not a camera id.");
        }

        return _cameras[cameraId - HighwayLayout.FirstCamera];
    }
}
