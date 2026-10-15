using HighwaySpeedEnforcement.Client.Application;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.Application;

/// <summary>A scripted <see cref="ISnapshotClient"/>: returns, fails, or blocks until released (honoring cancellation).</summary>
internal sealed class FakeSnapshotClient : ISnapshotClient
{
    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
    private Func<CancellationToken, Task<TrafficSnapshot>> _behavior = _ => throw new InvalidOperationException("No behavior scripted.");
    private TaskCompletionSource<TrafficSnapshot>? _blocked;

    public int Calls { get; private set; }

    public void Return(TrafficSnapshot snapshot)
    {
        _behavior = _ => Task.FromResult(snapshot);
    }

    public void Fail(string message)
    {
        _behavior = _ => Task.FromException<TrafficSnapshot>(new SnapshotUnavailableException(message));
    }

    /// <summary>Every request waits until <see cref="Release"/>, or until its token is cancelled.</summary>
    public void Block()
    {
        var blocked = new TaskCompletionSource<TrafficSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        _blocked = blocked;
        _behavior = cancellationToken =>
        {
            cancellationToken.Register(() => blocked.TrySetCanceled(cancellationToken));
            return blocked.Task;
        };
    }

    /// <summary>Completes the blocked request with <paramref name="snapshot"/>; later requests return it immediately.</summary>
    public void Release(TrafficSnapshot snapshot)
    {
        Return(snapshot);
        _blocked?.TrySetResult(snapshot);
    }

    public Task<TrafficSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Calls++;

            foreach (var waiter in _waiters.Where(waiter => waiter.Count <= Calls).ToArray())
            {
                waiter.Signal.TrySetResult();
                _waiters.Remove(waiter);
            }
        }

        return _behavior(cancellationToken);
    }

    /// <summary>Completes once at least <paramref name="count"/> requests were started; fails after a bounded wait.</summary>
    public Task WhenCalledAsync(int count, CancellationToken cancellationToken)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            if (Calls >= count)
            {
                return Task.CompletedTask;
            }

            _waiters.Add((count, signal));
        }

        return signal.Task.WaitAsync(WaitBound, cancellationToken);
    }
}
