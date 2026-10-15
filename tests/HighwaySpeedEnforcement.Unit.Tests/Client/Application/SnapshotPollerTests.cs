using HighwaySpeedEnforcement.Client.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.Application;

public sealed class SnapshotPollerTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(3);

    private readonly FakeTimeProvider _time = new(ClientTestData.GeneratedAt);
    private readonly FakeSnapshotClient _client = new();
    private readonly FakeLogger<SnapshotPoller> _logger = new();
    private readonly ClientSettings _settings = new();
    private readonly Lock _gate = new();
    private readonly List<DashboardState> _states = [];
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
    private readonly CancellationTokenSource _stop = new();
    private SnapshotPoller? _poller;
    private Task? _run;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        if (_run is not null)
        {
            await _run.WaitAsync(WaitBound, CancellationToken.None);
        }

        _stop.Dispose();
    }

    [Fact]
    [Trait("BR", "BR-UI-02")]
    public async Task First_request_is_made_at_start()
    {
        var snapshot = ClientTestData.Snapshot(_time.GetUtcNow());
        _client.Return(snapshot);

        Start();

        var state = await WhenStatesAsync(1);
        Assert.Equal(1, _client.Calls);
        Assert.Same(snapshot, state.Snapshot);
        Assert.True(state.HasData);
        Assert.False(state.IsStale);
        Assert.Equal(_time.GetUtcNow(), state.LastSuccessAt);
        Assert.Null(state.LastError);
        Assert.Same(state, _poller!.State);
    }

    [Fact]
    [Trait("AC", "AC-26")]
    [Trait("BR", "BR-UI-02")]
    public async Task One_request_per_period()
    {
        _client.Return(ClientTestData.Snapshot(_time.GetUtcNow()));
        Start();
        await _client.WhenCalledAsync(1, Token);

        _time.Advance(Period);
        await _client.WhenCalledAsync(2, Token);

        _time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(2, _client.Calls);

        _time.Advance(TimeSpan.FromSeconds(1.5));
        await _client.WhenCalledAsync(3, Token);
        Assert.Equal(3, _client.Calls);
        await WhenStatesAsync(3);
    }

    [Fact]
    [Trait("AC", "AC-26")]
    [Trait("BR", "BR-UI-02")]
    public async Task Blocked_request_prevents_the_next_and_missed_periods_are_not_queued()
    {
        _client.Block();
        Start();
        await _client.WhenCalledAsync(1, Token);

        _time.Advance(Period * 3);
        Assert.Equal(1, _client.Calls);
        Assert.Empty(_states);

        _client.Release(ClientTestData.Snapshot(_time.GetUtcNow()));
        await _client.WhenCalledAsync(2, Token);
        await WhenStatesAsync(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, _client.Calls);
    }

    [Fact]
    [Trait("AC", "AC-27")]
    [Trait("BR", "BR-UI-03")]
    public async Task Failure_keeps_the_last_snapshot_marks_it_stale_and_recovery_clears_it()
    {
        var first = ClientTestData.Snapshot(_time.GetUtcNow());
        _client.Return(first);
        Start();
        var healthy = await WhenStatesAsync(1);
        var successAt = _time.GetUtcNow();

        _client.Fail("connection refused");
        _time.Advance(Period);
        var stale = await WhenStatesAsync(2);

        Assert.True(stale.IsStale);
        Assert.Same(first, stale.Snapshot);
        Assert.Equal(healthy.GlobalStatus, stale.GlobalStatus);
        Assert.Equal(healthy.CameraStatuses, stale.CameraStatuses);
        Assert.Equal(successAt, stale.LastSuccessAt);
        Assert.Equal("connection refused", stale.LastError);

        _time.Advance(Period);
        var stillStale = await WhenStatesAsync(3);
        Assert.True(stillStale.IsStale);
        Assert.Equal(3, _client.Calls);

        var second = ClientTestData.Snapshot(_time.GetUtcNow());
        _client.Return(second);
        _time.Advance(Period);
        var recovered = await WhenStatesAsync(4);

        Assert.False(recovered.IsStale);
        Assert.Same(second, recovered.Snapshot);
        Assert.Equal(_time.GetUtcNow(), recovered.LastSuccessAt);
        Assert.Null(recovered.LastError);
    }

    [Fact]
    [Trait("AC", "AC-27")]
    [Trait("BR", "BR-UI-03")]
    public async Task Failure_before_the_first_success_is_stale_without_data()
    {
        _client.Fail("connection refused");

        Start();

        var state = await WhenStatesAsync(1);
        Assert.True(state.IsStale);
        Assert.False(state.HasData);
        Assert.Null(state.LastSuccessAt);
        Assert.Equal("connection refused", state.LastError);
    }

    [Fact]
    [Trait("BR", "BR-UI-03")]
    public async Task Request_exceeding_the_timeout_is_a_failure()
    {
        _client.Block();
        Start();
        await _client.WhenCalledAsync(1, Token);

        _time.Advance(_settings.RequestTimeout);

        var state = await WhenStatesAsync(1);
        Assert.True(state.IsStale);
        Assert.Contains("timed out", state.LastError, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("AC", "AC-28")]
    [Trait("AC", "AC-32")]
    [Trait("BR", "BR-UI-04")]
    public async Task State_carries_the_liveness_of_every_board()
    {
        var generatedAt = _time.GetUtcNow();
        var snapshot = ClientTestData.Snapshot(generatedAt, camera => camera switch
        {
            0 => generatedAt.AddSeconds(-0.3),
            7 => generatedAt.AddSeconds(-128),
            10 => null,
            _ => generatedAt.AddSeconds(-1),
        });
        _client.Return(snapshot);

        Start();

        var state = await WhenStatesAsync(1);
        Assert.Equal(new BoardStatus(Liveness.Live, TimeSpan.FromSeconds(0.3)), state.GlobalStatus);
        Assert.Equal(new BoardStatus(Liveness.Live, TimeSpan.FromSeconds(1)), state.CameraStatuses[0]);
        Assert.Equal(new BoardStatus(Liveness.NotLive, TimeSpan.FromSeconds(128)), state.CameraStatuses[6]);
        Assert.Equal(BoardStatus.NoData, state.CameraStatuses[9]);
        Assert.Equal(10, state.CameraStatuses.Count);
    }

    [Fact]
    public async Task Cancellation_completes_the_loop_while_waiting_for_the_next_period()
    {
        _client.Return(ClientTestData.Snapshot(_time.GetUtcNow()));
        Start();
        await WhenStatesAsync(1);

        await _stop.CancelAsync();

        await _run!.WaitAsync(WaitBound, Token);
        Assert.True(_run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Cancellation_completes_the_loop_while_a_request_is_running()
    {
        _client.Block();
        Start();
        await _client.WhenCalledAsync(1, Token);

        await _stop.CancelAsync();

        await _run!.WaitAsync(WaitBound, Token);
        Assert.True(_run.IsCompletedSuccessfully);
        Assert.Empty(_states);
    }

    [Fact]
    [Trait("BR", "BR-UI-03")]
    public async Task Logs_only_the_transition_to_failing_and_the_recovery()
    {
        _client.Return(ClientTestData.Snapshot(_time.GetUtcNow()));
        Start();
        await WhenStatesAsync(1);
        Assert.Empty(_logger.Collector.GetSnapshot());

        _client.Fail("connection refused");
        _time.Advance(Period);
        await WhenStatesAsync(2);
        _time.Advance(Period);
        await WhenStatesAsync(3);

        var afterFailures = _logger.Collector.GetSnapshot();
        var warning = Assert.Single(afterFailures);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("connection refused", warning.Message, StringComparison.Ordinal);

        _client.Return(ClientTestData.Snapshot(_time.GetUtcNow()));
        _time.Advance(Period);
        await WhenStatesAsync(4);
        _time.Advance(Period);
        await WhenStatesAsync(5);

        var all = _logger.Collector.GetSnapshot();
        Assert.Equal(2, all.Count);
        Assert.Equal(LogLevel.Information, all[1].Level);
    }

    [Fact]
    [Trait("BR", "BR-UI-02")]
    public async Task A_shorter_period_chosen_while_running_takes_effect_at_once()
    {
        _client.Return(ClientTestData.Snapshot(_time.GetUtcNow()));
        Start();
        await _client.WhenCalledAsync(1, Token);
        Assert.Equal(Period, _poller!.Interval);

        _poller.SetInterval(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), _poller.Interval);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _client.WhenCalledAsync(2, Token);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _client.WhenCalledAsync(3, Token);
        Assert.Equal(3, _client.Calls);
        Assert.Contains(_logger.Collector.GetSnapshot(), record => record.Message.Contains("Polling period changed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "BR-UI-02")]
    public async Task A_longer_period_chosen_while_running_is_respected()
    {
        _client.Return(ClientTestData.Snapshot(_time.GetUtcNow()));
        Start();
        await _client.WhenCalledAsync(1, Token);

        _poller!.SetInterval(TimeSpan.FromSeconds(5));
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(1, _client.Calls);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _client.WhenCalledAsync(2, Token);
    }

    [Fact]
    public void The_period_is_the_configured_one_until_changed_and_must_be_positive()
    {
        var poller = new SnapshotPoller(_client, new ClientSettings { PollInterval = TimeSpan.FromSeconds(4) }, _time, _logger);

        Assert.Equal(TimeSpan.FromSeconds(4), poller.Interval);
        Assert.Throws<ArgumentOutOfRangeException>(() => poller.SetInterval(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => poller.SetInterval(TimeSpan.FromSeconds(-1)));

        poller.SetInterval(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), poller.Interval);
    }

    [Fact]
    public void The_offered_periods_are_the_standard_ones_plus_the_configured_one()
    {
        Assert.Equal([1, 2, 3, 5], PollIntervals.OptionsFor(Period).Select(option => (int)option.TotalSeconds));
        Assert.Equal([1, 2, 3, 4, 5], PollIntervals.OptionsFor(TimeSpan.FromSeconds(4)).Select(option => (int)option.TotalSeconds));
        Assert.Equal(0.5, PollIntervals.OptionsFor(TimeSpan.FromSeconds(0.5))[0].TotalSeconds);
    }

    private void Start()
    {
        _poller = new SnapshotPoller(_client, _settings, _time, _logger);
        _poller.StateChanged += (_, state) =>
        {
            lock (_gate)
            {
                _states.Add(state);

                foreach (var waiter in _waiters.Where(waiter => waiter.Count <= _states.Count).ToArray())
                {
                    waiter.Signal.TrySetResult();
                    _waiters.Remove(waiter);
                }
            }
        };
        _run = Task.Run(() => _poller.RunAsync(_stop.Token), Token);
    }

    /// <summary>The <paramref name="count"/>-th raised state, once it was raised.</summary>
    private async Task<DashboardState> WhenStatesAsync(int count)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            if (_states.Count < count)
            {
                _waiters.Add((count, signal));
            }
            else
            {
                signal.SetResult();
            }
        }

        await signal.Task.WaitAsync(WaitBound, Token);

        lock (_gate)
        {
            return _states[count - 1];
        }
    }
}
