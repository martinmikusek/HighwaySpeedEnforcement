using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Application;

public sealed class SnapshotServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly long Second = TimeSpan.FromSeconds(1).Ticks;

    [Fact]
    [Trait("BR", "BR-LB-01")]
    [Trait("BR", "BR-LB-02")]
    public void BoardSet_has_ten_camera_boards_and_a_global_board_with_the_policy()
    {
        var policy = new ProcessorSettings().ToLivenessPolicy();

        var boards = new BoardSet(policy);

        Assert.Equal(10, boards.Cameras.Count);
        Assert.Same(policy, boards.Policy);
        Assert.Same(boards.Cameras[0], boards.Camera(1));
        Assert.Same(boards.Cameras[9], boards.Camera(10));
        Assert.Equal(11, boards.Cameras.Append(boards.Global).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => boards.Camera(11));
    }

    [Fact]
    [Trait("AC", "AC-22")]
    [Trait("BR", "BR-API-01")]
    public void Empty_snapshot_has_ten_empty_boards_null_latest_event_times_and_the_clock_time()
    {
        var service = new SnapshotService(new BoardSet(new ProcessorSettings().ToLivenessPolicy()), new FakeTimeProvider(Now));

        var snapshot = service.Take();

        Assert.Equal(Now, snapshot.GeneratedAt);
        Assert.Equal(10, snapshot.CameraBoards.Count);
        Assert.All(snapshot.CameraBoards, board => Assert.Empty(board.Entries));
        Assert.All(snapshot.CameraBoards, board => Assert.Null(board.LatestEventTicks));
        Assert.Empty(snapshot.GlobalBoard.Entries);
        Assert.Null(snapshot.GlobalBoard.LatestEventTicks);
    }

    [Fact]
    [Trait("BR", "BR-API-02")]
    public void Snapshot_is_a_detached_copy()
    {
        var boards = new BoardSet(new ProcessorSettings().ToLivenessPolicy());
        var service = new SnapshotService(boards, new FakeTimeProvider(Now));
        boards.Camera(5).Update(10 * Second, new BoardEntry("A", 150, 10 * Second, 12 * Second, 1));
        boards.Global.Update(10 * Second, new BoardEntry("A", 140, 10 * Second, 15 * Second, 3));

        var snapshot = service.Take();
        boards.Camera(5).Update(20 * Second, new BoardEntry("B", 160, 20 * Second, 22 * Second, 1));
        boards.Global.Remove("A");

        Assert.Equal("A", Assert.Single(snapshot.CameraBoards[4].Entries).Entry.Plate);
        Assert.Equal(10 * Second, snapshot.CameraBoards[4].LatestEventTicks);
        Assert.Equal("A", Assert.Single(snapshot.GlobalBoard.Entries).Entry.Plate);
    }
}
