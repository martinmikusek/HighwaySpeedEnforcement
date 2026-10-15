using HighwaySpeedEnforcement.Processor.Application;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>
/// An <see cref="IIngestSink"/> that records what the source hands it and can hold a submission open through
/// <see cref="Gate"/>, which lets a test observe that the message is not acknowledged before the submission completes.
/// </summary>
public sealed class RecordingSink : IIngestSink, IIngestMetrics
{
    private readonly Lock _lock = new();
    private readonly List<IncomingMessage> _messages = [];
    private readonly List<RejectReason> _undecodable = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _ignored;

    /// <summary>Awaited inside <see cref="SubmitAsync"/> before the message is recorded; null means no gate.</summary>
    public Func<IncomingMessage, CancellationToken, Task>? Gate { get; set; }

    public IReadOnlyList<IncomingMessage> Messages
    {
        get
        {
            lock (_lock)
            {
                return _messages.ToArray();
            }
        }
    }

    public IReadOnlyList<RejectReason> Undecodable
    {
        get
        {
            lock (_lock)
            {
                return _undecodable.ToArray();
            }
        }
    }

    public int IgnoredCount => Volatile.Read(ref _ignored);

    public async ValueTask SubmitAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        if (Gate is { } gate)
        {
            await gate(message, cancellationToken);
        }

        lock (_lock)
        {
            _messages.Add(message);
            Signal();
        }
    }

    public void ReportUndecodable(RejectReason reason)
    {
        lock (_lock)
        {
            _undecodable.Add(reason);
            Signal();
        }
    }

    public void Received()
    {
    }

    public void Accepted()
    {
    }

    public void Rejected(RejectReason reason)
    {
    }

    public void Latency(TimeSpan latency)
    {
    }

    public void Ignored()
    {
        Interlocked.Increment(ref _ignored);
        lock (_lock)
        {
            Signal();
        }
    }

    /// <summary>Completes once <paramref name="condition"/> holds; re-evaluated after every recorded event.</summary>
    public async Task WaitUntilAsync(Func<RecordingSink, bool> condition, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;

            lock (_lock)
            {
                if (condition(this))
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }

    public Task WaitForMessagesAsync(int count, CancellationToken cancellationToken)
    {
        return WaitUntilAsync(sink => sink._messages.Count >= count, cancellationToken);
    }

    private void Signal()
    {
        var previous = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }
}
