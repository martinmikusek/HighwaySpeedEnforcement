using HighwaySpeedEnforcement.Generator.Domain;
using Microsoft.Extensions.Hosting;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

/// <summary>Records the ordered calls of the publisher and the store, so that tests can assert what happened in which order.</summary>
internal sealed class CallLog
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly List<(int Count, TaskCompletionSource Waiter)> _waiters = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToArray();
            }
        }
    }

    public void Add(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);

            foreach (var (count, waiter) in _waiters.Where(pair => pair.Count <= _calls.Count).ToArray())
            {
                waiter.TrySetResult();
                _waiters.RemoveAll(pair => pair.Count == count);
            }
        }
    }

    /// <summary>Completes once at least <paramref name="count"/> calls were recorded; bounded so a wrong test fails instead of hanging.</summary>
    public Task WhenCallsAsync(int count, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_calls.Count >= count)
            {
                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, waiter));

            return waiter.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}

internal sealed class FakePublisher : IMeasurementPublisher
{
    private readonly CallLog _log;
    private readonly Lock _lock = new();
    private readonly List<TickOutput> _started = [];
    private readonly List<TickOutput> _published = [];
    private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakePublisher(CallLog log)
    {
        _log = log;
        _gate.SetResult();
    }

    public IReadOnlyList<TickOutput> Started
    {
        get
        {
            lock (_lock)
            {
                return _started.ToArray();
            }
        }
    }

    public IReadOnlyList<TickOutput> Published
    {
        get
        {
            lock (_lock)
            {
                return _published.ToArray();
            }
        }
    }

    public bool Disposed { get; private set; }

    /// <summary>Every publish from now on waits until <see cref="Release"/>.</summary>
    public void Block()
    {
        lock (_lock)
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            _gate.TrySetResult();
        }
    }

    public async Task PublishTickAsync(TickOutput tick, CancellationToken cancellationToken)
    {
        Task gate;

        lock (_lock)
        {
            _started.Add(tick);
            gate = _gate.Task;
        }

        _log.Add($"publish-start {tick.TickNumber}");
        await gate.WaitAsync(cancellationToken);

        lock (_lock)
        {
            _published.Add(tick);
        }

        _log.Add($"publish-done {tick.TickNumber}");
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeStore : IPlateStateStore
{
    private readonly CallLog _log;

    public FakeStore(CallLog log)
    {
        _log = log;
    }

    public PlateState? Stored { get; set; }

    public int LoadCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public PlateState? Load()
    {
        LoadCalls++;
        _log.Add("load");
        return Stored;
    }

    public void Save(PlateState state)
    {
        SaveCalls++;
        Stored = state;
        _log.Add($"save {state.NextIndex}");
    }
}

internal sealed class FakeLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    public CancellationToken ApplicationStarted => _started.Token;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => _stopped.Token;

    public bool StopRequested => _stopping.IsCancellationRequested;

    public void StopApplication()
    {
        _stopping.Cancel();
    }
}
