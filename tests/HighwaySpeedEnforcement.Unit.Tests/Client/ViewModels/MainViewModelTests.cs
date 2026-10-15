using System.ComponentModel;
using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.ViewModels;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.ViewModels;

public sealed class MainViewModelTests
{
    private static readonly DisplayFormatter Formatter = new(EntryViewModelTests.PlusTwo);
    private static readonly DateTimeOffset GeneratedAt = ClientTestData.GeneratedAt;
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(3);

    private readonly FakeStateSource _source = new();

    [Fact]
    [Trait("BR", "BR-UI-03")]
    public void Initial_state_is_waiting_for_data()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);

        Assert.Equal("Waiting for data", vm.StatusText);
        Assert.False(vm.IsStale);
        Assert.False(vm.HasData);
        Assert.Equal("", vm.LastSuccessText);
        Assert.Equal(1, vm.SelectedCamera);
        Assert.Equal(Enumerable.Range(1, 10), vm.CameraOptions);
        Assert.Equal("Global leaderboard", vm.Global.Title);
        Assert.Equal("Camera 1", vm.Camera.Title);
        Assert.Equal("no data yet", vm.Global.EmptyText);
        Assert.Equal("no data yet", vm.Camera.EmptyText);
    }

    [Fact]
    [Trait("AC", "AC-25")]
    [Trait("BR", "BR-UI-01")]
    public void State_fills_the_global_board_and_the_selected_camera_board()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);

        _source.Raise(Healthy(Snapshot()));

        Assert.Equal(["4S7 1234", "2AB 9876"], vm.Global.Entries.Select(row => row.NumberPlate));
        Assert.Equal(["144.4", "143.9"], vm.Global.Entries.Select(row => row.ScoreText));
        Assert.Equal(["6", "10"], vm.Global.Entries.Select(row => row.DetailText));
        Assert.Equal(["C1-A"], vm.Camera.Entries.Select(row => row.NumberPlate));
        Assert.Equal(["152.4"], vm.Camera.Entries.Select(row => row.ScoreText));
        Assert.True(vm.HasData);
        Assert.False(vm.IsStale);
        Assert.Equal("Connected, snapshot generated 16:03:10.0", vm.StatusText);
        Assert.Equal("Last success 16:03:11", vm.LastSuccessText);
    }

    [Fact]
    [Trait("AC", "AC-25")]
    [Trait("BR", "BR-UI-01")]
    public void Selecting_a_camera_renders_it_from_the_last_state_without_a_request()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);
        _source.Raise(Healthy(Snapshot()));
        var statesBefore = _source.State;

        vm.SelectedCamera = 5;

        Assert.Equal("Camera 5", vm.Camera.Title);
        Assert.Equal(["C5-A", "C5-B"], vm.Camera.Entries.Select(row => row.NumberPlate));
        Assert.Same(statesBefore, _source.State);

        vm.SelectedCamera = 10;

        Assert.Equal("Camera 10", vm.Camera.Title);
        Assert.Empty(vm.Camera.Entries);
        Assert.Equal("no data yet", vm.Camera.EmptyText);
    }

    [Fact]
    [Trait("AC", "AC-25")]
    public void Camera_selection_survives_the_next_state()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);
        _source.Raise(Healthy(Snapshot()));
        vm.SelectedCamera = 5;

        _source.Raise(Healthy(Snapshot()));

        Assert.Equal(5, vm.SelectedCamera);
        Assert.Equal("Camera 5", vm.Camera.Title);
        Assert.Equal(["C5-A", "C5-B"], vm.Camera.Entries.Select(row => row.NumberPlate));
    }

    [Fact]
    [Trait("AC", "AC-27")]
    [Trait("BR", "BR-UI-03")]
    public void Stale_state_keeps_the_rows_and_shows_the_last_success_and_the_reason_until_recovery()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);
        var healthy = Healthy(Snapshot());
        _source.Raise(healthy);

        _source.Raise(healthy with { IsStale = true, LastError = "processor unreachable (ConnectionError)" });

        Assert.True(vm.IsStale);
        Assert.True(vm.HasData);
        Assert.Equal(["4S7 1234", "2AB 9876"], vm.Global.Entries.Select(row => row.NumberPlate));
        Assert.Equal(["C1-A"], vm.Camera.Entries.Select(row => row.NumberPlate));
        Assert.Equal("Connection lost, showing data from 16:03:11: processor unreachable (ConnectionError)", vm.StatusText);
        Assert.Equal("Last success 16:03:11", vm.LastSuccessText);
        Assert.True(vm.Global.IsLive);

        _source.Raise(Healthy(Snapshot(), GeneratedAt.AddSeconds(7)));

        Assert.False(vm.IsStale);
        Assert.StartsWith("Connected", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal("Last success 16:03:17", vm.LastSuccessText);
    }

    [Fact]
    [Trait("AC", "AC-27")]
    [Trait("BR", "BR-UI-03")]
    public void Failure_before_any_data_shows_the_reason_while_waiting()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);

        _source.Raise(DashboardState.Initial with { IsStale = true, LastError = "processor unreachable (ConnectionError)" });

        Assert.True(vm.IsStale);
        Assert.False(vm.HasData);
        Assert.Equal("Waiting for data: processor unreachable (ConnectionError)", vm.StatusText);
        Assert.Equal("", vm.LastSuccessText);
        Assert.Equal("no data yet", vm.Global.EmptyText);
    }

    [Fact]
    [Trait("AC", "AC-28")]
    [Trait("AC", "AC-32")]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-UI-04")]
    public void Each_displayed_board_carries_its_own_liveness()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);

        _source.Raise(Healthy(Snapshot()));

        Assert.True(vm.Global.IsLive);
        Assert.Equal("live", vm.Global.AgeText);
        Assert.True(vm.Camera.IsLive);

        vm.SelectedCamera = 7;
        Assert.False(vm.Camera.IsLive);
        Assert.True(vm.Camera.IsDimmed);
        Assert.Equal("not live, latest event 128.0 s old", vm.Camera.AgeText);

        vm.SelectedCamera = 5;
        Assert.True(vm.Camera.IsLive);
        Assert.Equal("", vm.Camera.EmptyText);

        vm.SelectedCamera = 3;
        Assert.True(vm.Camera.IsLive);
        Assert.Equal("no live entries", vm.Camera.EmptyText);

        vm.SelectedCamera = 10;
        Assert.Equal(Liveness.NoData, vm.Camera.Liveness);
        Assert.True(vm.Camera.IsDimmed);
        Assert.Equal("no data yet", vm.Camera.EmptyText);
    }

    [Fact]
    public void Properties_change_only_inside_the_dispatcher()
    {
        var dispatcher = new RecordingDispatcher();
        using var vm = new MainViewModel(_source, dispatcher, Formatter);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        vm.Global.PropertyChanged += (_, args) => changed.Add("Global." + args.PropertyName);
        vm.Global.Entries.CollectionChanged += (_, _) => changed.Add("Global.Entries");

        _source.Raise(Healthy(Snapshot()));

        Assert.Empty(changed);
        Assert.Empty(vm.Global.Entries);
        Assert.False(vm.HasData);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunAll();

        Assert.NotEmpty(changed);
        Assert.Contains(nameof(MainViewModel.HasData), changed);
        Assert.Contains("Global.Entries", changed);
        Assert.Equal(2, vm.Global.Entries.Count);
    }

    [Fact]
    public void State_raised_before_the_view_model_existed_is_applied_through_the_dispatcher()
    {
        _source.Raise(Healthy(Snapshot()));
        var dispatcher = new RecordingDispatcher();

        using var vm = new MainViewModel(_source, dispatcher, Formatter);

        Assert.False(vm.HasData);
        Assert.Equal(1, dispatcher.PendingCount);
        dispatcher.RunAll();
        Assert.True(vm.HasData);
    }

    [Fact]
    public void Dispose_unsubscribes_from_the_source()
    {
        var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);
        Assert.Equal(1, _source.SubscriberCount);

        vm.Dispose();
        vm.Dispose();

        Assert.Equal(0, _source.SubscriberCount);
        _source.Raise(Healthy(Snapshot()));
        Assert.False(vm.HasData);
    }

    [Fact]
    public void View_models_implement_change_notification_without_wpf()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);

        Assert.IsAssignableFrom<INotifyPropertyChanged>(vm);
        Assert.DoesNotContain(typeof(MainViewModel).Assembly.GetReferencedAssemblies(), name => name.Name!.StartsWith("PresentationFramework", StringComparison.Ordinal));
    }

    /// <summary>Camera 1 with one entry, camera 5 with two, camera 7 frozen (128 s), camera 10 without data; two global entries.</summary>
    private static TrafficSnapshot Snapshot()
    {
        return ClientTestData.Snapshot(GeneratedAt, camera => camera switch
            {
                0 => GeneratedAt.AddSeconds(-0.3),
                7 => GeneratedAt.AddSeconds(-128),
                10 => null,
                _ => GeneratedAt.AddSeconds(-0.6),
            })
            .WithCameraEntries(1, new CameraEntry(1, "C1-A", 152.4f, GeneratedAt.AddSeconds(-0.6)))
            .WithCameraEntries(5, new CameraEntry(1, "C5-A", 150.0f, GeneratedAt.AddSeconds(-0.6)), new CameraEntry(2, "C5-B", 140.0f, GeneratedAt.AddSeconds(-0.7)))
            .WithCameraEntries(7, new CameraEntry(1, "C7-A", 138.6f, GeneratedAt.AddSeconds(-128)))
            .WithGlobalEntries(
                new GlobalEntry(1, "4S7 1234", 144.35, 6, GeneratedAt.AddSeconds(-0.6)),
                new GlobalEntry(2, "2AB 9876", 143.9, 10, GeneratedAt.AddSeconds(-1.5)));
    }

    /// <summary>The state the poller builds for a successful poll at <paramref name="successAt"/> (default: one second after the snapshot).</summary>
    private static DashboardState Healthy(TrafficSnapshot snapshot, DateTimeOffset? successAt = null)
    {
        return new DashboardState(
            snapshot,
            LivenessEvaluator.Evaluate(snapshot.GeneratedAt, snapshot.GlobalBoard.LatestEvent, Threshold),
            snapshot.CameraBoards.Select(board => LivenessEvaluator.Evaluate(snapshot.GeneratedAt, board.LatestEvent, Threshold)).ToArray(),
            false,
            successAt ?? GeneratedAt.AddSeconds(1),
            null);
    }
}
