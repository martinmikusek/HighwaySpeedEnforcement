using System.ComponentModel;
using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.ViewModels;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.ViewModels;

public sealed class BoardViewModelTests
{
    private static readonly DisplayFormatter Formatter = new(EntryViewModelTests.PlusTwo);
    private static readonly DateTimeOffset GeneratedAt = ClientTestData.GeneratedAt;

    [Fact]
    [Trait("AC", "AC-28")]
    public void New_board_has_no_data()
    {
        var board = new BoardViewModel("Camera 1", Formatter);

        Assert.Equal("Camera 1", board.Title);
        Assert.Empty(board.Entries);
        Assert.Equal(Liveness.NoData, board.Liveness);
        Assert.False(board.IsLive);
        Assert.True(board.IsDimmed);
        Assert.Equal("Latest event: none", board.LatestEventText);
        Assert.Equal("", board.AgeText);
        Assert.Equal("no data yet", board.EmptyText);
        Assert.True(board.IsEmpty);
    }

    [Fact]
    [Trait("AC", "AC-28")]
    [Trait("BR", "BR-UI-04")]
    public void Board_without_a_watermark_reads_no_data_yet()
    {
        var board = new BoardViewModel("Camera 10", Formatter);

        board.Update(new CameraBoard(10, null, []), BoardStatus.NoData);

        Assert.Equal(Liveness.NoData, board.Liveness);
        Assert.True(board.IsDimmed);
        Assert.Equal("Latest event: none", board.LatestEventText);
        Assert.Equal("no data yet", board.EmptyText);
    }

    [Fact]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-UI-04")]
    public void Empty_board_with_a_watermark_reads_no_live_entries()
    {
        var board = new BoardViewModel("Camera 5", Formatter);

        board.Update(new CameraBoard(5, GeneratedAt.AddSeconds(-0.5), []), new BoardStatus(Liveness.Live, TimeSpan.FromSeconds(0.5)));

        Assert.Empty(board.Entries);
        Assert.True(board.IsLive);
        Assert.False(board.IsDimmed);
        Assert.Equal("Latest event: 16:03:09.5", board.LatestEventText);
        Assert.Equal("live", board.AgeText);
        Assert.Equal("no live entries", board.EmptyText);
        Assert.True(board.IsEmpty);
    }

    [Fact]
    [Trait("AC", "AC-32")]
    [Trait("BR", "BR-UI-04")]
    public void Live_board_shows_its_rows_and_no_empty_text()
    {
        var board = new BoardViewModel("Global leaderboard", Formatter);
        var entries = new[]
        {
            new GlobalEntry(1, "4S7 1234", 144.35, 6, GeneratedAt.AddSeconds(-0.6)),
            new GlobalEntry(2, "2AB 9876", 143.9, 10, GeneratedAt.AddSeconds(-1.5)),
        };

        board.Update(new GlobalBoard(GeneratedAt.AddSeconds(-0.3), entries), new BoardStatus(Liveness.Live, TimeSpan.FromSeconds(0.3)));

        Assert.Equal(["4S7 1234", "2AB 9876"], board.Entries.Select(row => row.NumberPlate));
        Assert.Equal(["144.4", "143.9"], board.Entries.Select(row => row.ScoreText));
        Assert.Equal(["6", "10"], board.Entries.Select(row => row.DetailText));
        Assert.True(board.IsLive);
        Assert.False(board.IsDimmed);
        Assert.Equal("", board.EmptyText);
        Assert.False(board.IsEmpty);
    }

    [Fact]
    [Trait("AC", "AC-28")]
    [Trait("BR", "BR-UI-04")]
    public void Board_that_is_not_live_is_dimmed_and_shows_the_age_of_its_data()
    {
        var board = new BoardViewModel("Camera 7", Formatter);
        var entries = new[] { new CameraEntry(1, "3B2 5555", 138.6f, GeneratedAt.AddSeconds(-128)) };

        board.Update(new CameraBoard(7, GeneratedAt.AddSeconds(-12.4), entries), new BoardStatus(Liveness.NotLive, TimeSpan.FromSeconds(12.4)));

        Assert.Single(board.Entries);
        Assert.False(board.IsLive);
        Assert.True(board.IsDimmed);
        Assert.Equal("Latest event: 16:02:57.6", board.LatestEventText);
        Assert.Equal("not live, latest event 12.4 s old", board.AgeText);
        Assert.Equal("", board.EmptyText);
    }

    [Fact]
    public void Update_replaces_the_rows_and_notifies_the_derived_properties()
    {
        var board = new BoardViewModel("Camera 1", Formatter);
        board.Update(new CameraBoard(1, GeneratedAt, [new CameraEntry(1, "OLD", 100f, GeneratedAt)]), new BoardStatus(Liveness.Live, TimeSpan.Zero));
        var changed = new List<string?>();
        board.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        board.Update(new CameraBoard(1, GeneratedAt.AddSeconds(-5), [new CameraEntry(1, "NEW", 110f, GeneratedAt)]), new BoardStatus(Liveness.NotLive, TimeSpan.FromSeconds(5)));

        Assert.Equal("NEW", Assert.Single(board.Entries).NumberPlate);
        Assert.Contains(nameof(BoardViewModel.Liveness), changed);
        Assert.Contains(nameof(BoardViewModel.IsLive), changed);
        Assert.Contains(nameof(BoardViewModel.IsDimmed), changed);
        Assert.Contains(nameof(BoardViewModel.AgeText), changed);
        Assert.Contains(nameof(BoardViewModel.LatestEventText), changed);
    }

    [Fact]
    public void Board_view_model_implements_change_notification()
    {
        Assert.IsAssignableFrom<INotifyPropertyChanged>(new BoardViewModel("x", Formatter));
    }
}
