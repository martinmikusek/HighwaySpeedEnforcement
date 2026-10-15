using Microsoft.Extensions.Logging;

namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>
/// The polling loop (BR-UI-02): one request at start, then one per <see cref="ClientSettings.PollInterval"/>, never two at
/// once (the next period is awaited only after the request finished; periods missed meanwhile are coalesced by the
/// <see cref="PeriodicTimer"/>). Owns the <see cref="DashboardState"/> and raises <see cref="StateChanged"/> after every poll
/// on the loop's own thread.
/// </summary>
public sealed class SnapshotPoller : IDashboardStateSource, IPollIntervalControl
{
    private readonly ISnapshotClient _client;
    private readonly ClientSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SnapshotPoller> _logger;
    private readonly Lock _intervalLock = new();
    private TimeSpan _interval;
    private PeriodicTimer? _timer;

    public SnapshotPoller(ISnapshotClient client, ClientSettings settings, TimeProvider timeProvider, ILogger<SnapshotPoller> logger)
    {
        _client = client;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
        _interval = settings.PollInterval;
    }

    public event EventHandler<DashboardState>? StateChanged;

    public DashboardState State { get; private set; } = DashboardState.Initial;

    public TimeSpan Interval
    {
        get
        {
            lock (_intervalLock)
            {
                return _interval;
            }
        }
    }

    public void SetInterval(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        lock (_intervalLock)
        {
            if (_interval == interval)
            {
                return;
            }

            _logger.LogInformation("Polling period changed from {Old} s to {New} s", _interval.TotalSeconds, interval.TotalSeconds);
            _interval = interval;

            if (_timer is not null)
            {
                _timer.Period = interval;
            }
        }
    }

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled, then completes without throwing.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = StartTimer();

            try
            {
                do
                {
                    await PollOnceAsync(cancellationToken);
                }
                while (await timer.WaitForNextTickAsync(cancellationToken));
            }
            finally
            {
                lock (_intervalLock)
                {
                    _timer = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private PeriodicTimer StartTimer()
    {
        lock (_intervalLock)
        {
            _timer = new PeriodicTimer(_interval, _timeProvider);

            return _timer;
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        TrafficSnapshot snapshot;

        try
        {
            using var timeout = new CancellationTokenSource(_settings.RequestTimeout, _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            snapshot = await _client.GetSnapshotAsync(linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Fail($"request timed out after {_settings.RequestTimeout.TotalSeconds:0.#} s", null);
            return;
        }
        catch (SnapshotUnavailableException exception)
        {
            Fail(exception.Message, exception);
            return;
        }

        Succeed(snapshot);
    }

    private void Succeed(TrafficSnapshot snapshot)
    {
        var threshold = _settings.LiveLagThreshold;
        var cameraStatuses = snapshot.CameraBoards
            .Select(board => LivenessEvaluator.Evaluate(snapshot.GeneratedAt, board.LatestEvent, threshold))
            .ToArray();
        var globalStatus = LivenessEvaluator.Evaluate(snapshot.GeneratedAt, snapshot.GlobalBoard.LatestEvent, threshold);

        if (State.IsStale)
        {
            _logger.LogInformation("Snapshot polling recovered after: {LastError}", State.LastError);
        }

        Publish(new DashboardState(snapshot, globalStatus, cameraStatuses, false, _timeProvider.GetUtcNow(), null));
    }

    private void Fail(string reason, Exception? exception)
    {
        if (!State.IsStale)
        {
            _logger.LogWarning(exception, "Snapshot polling failed, showing the last snapshot as stale: {Reason}", reason);
        }

        Publish(State with { IsStale = true, LastError = reason });
    }

    private void Publish(DashboardState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
