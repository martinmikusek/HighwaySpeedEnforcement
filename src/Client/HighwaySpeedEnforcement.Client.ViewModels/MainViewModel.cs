using CommunityToolkit.Mvvm.ComponentModel;
using HighwaySpeedEnforcement.Client.Application;

namespace HighwaySpeedEnforcement.Client.ViewModels;

/// <summary>
/// The dashboard (BR-UI-01): the global board and the board of the selected camera, rendered from the last
/// <see cref="DashboardState"/>. The state arrives on the poller's thread; every bound property changes only inside
/// <see cref="IUiDispatcher.Post"/>. Selecting a camera re-renders from the last state without a request (AC-25).
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDashboardStateSource _source;
    private readonly IUiDispatcher _dispatcher;
    private readonly DisplayFormatter _formatter;
    private readonly IPollIntervalControl? _pollInterval;
    private DashboardState _lastState = DashboardState.Initial;
    private bool _disposed;

    [ObservableProperty]
    private int _selectedCamera = 1;

    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _statusText = "Waiting for data";

    [ObservableProperty]
    private string _lastSuccessText = "";

    [ObservableProperty]
    private TimeSpan _selectedPollInterval;

    public MainViewModel(IDashboardStateSource source, IUiDispatcher dispatcher, DisplayFormatter formatter, IPollIntervalControl? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _dispatcher = dispatcher;
        _formatter = formatter;
        _pollInterval = pollInterval;

        if (pollInterval is not null)
        {
            _selectedPollInterval = pollInterval.Interval;
            PollIntervalOptions = PollIntervals.OptionsFor(pollInterval.Interval);
        }

        Global = new BoardViewModel("Global leaderboard", formatter);
        Camera = new BoardViewModel(CameraTitle(SelectedCamera), formatter);

        _source.StateChanged += OnStateChanged;

        // The poller may have polled before the view model existed.
        if (!ReferenceEquals(_source.State, DashboardState.Initial))
        {
            OnStateChanged(_source, _source.State);
        }
    }

    public BoardViewModel Global { get; }

    public BoardViewModel Camera { get; }

    public IReadOnlyList<int> CameraOptions { get; } = Enumerable.Range(1, TrafficSnapshot.CameraCount).ToArray();

    /// <summary>The refresh periods the user can choose (empty when the polling period cannot be changed).</summary>
    public IReadOnlyList<TimeSpan> PollIntervalOptions { get; } = [];

    public bool CanChangePollInterval => _pollInterval is not null;

    public void Dispose()
    {
        if (!_disposed)
        {
            _source.StateChanged -= OnStateChanged;
            _disposed = true;
        }
    }

    partial void OnSelectedPollIntervalChanged(TimeSpan value)
    {
        _pollInterval?.SetInterval(value);
    }

    partial void OnSelectedCameraChanged(int value)
    {
        RenderCamera();
    }

    private static string CameraTitle(int camera)
    {
        return $"Camera {camera}";
    }

    private void OnStateChanged(object? sender, DashboardState state)
    {
        _dispatcher.Post(() => Apply(state));
    }

    private void Apply(DashboardState state)
    {
        _lastState = state;

        if (state.Snapshot is { } snapshot)
        {
            Global.Update(snapshot.GlobalBoard, state.GlobalStatus);
        }

        RenderCamera();

        HasData = state.HasData;
        IsStale = state.IsStale;
        LastSuccessText = state.LastSuccessAt is { } success ? $"Last success {_formatter.TimeOfDay(success)}" : "";
        StatusText = (state.HasData, state.IsStale) switch
        {
            (false, false) => "Waiting for data",
            (false, true) => $"Waiting for data: {state.LastError}",
            (true, false) => $"Connected, snapshot generated {_formatter.Time(state.Snapshot!.GeneratedAt)}",
            (true, true) => $"Connection lost, showing data from {_formatter.TimeOfDay(state.LastSuccessAt!.Value)}: {state.LastError}",
        };
    }

    private void RenderCamera()
    {
        Camera.Title = CameraTitle(SelectedCamera);

        if (_lastState.Snapshot is { } snapshot)
        {
            Camera.Update(snapshot.CameraBoards[SelectedCamera - 1], _lastState.CameraStatuses[SelectedCamera - 1]);
        }
    }
}
