using System.Threading.Channels;
using HighwaySpeedEnforcement.Processor.Domain;
using Microsoft.Extensions.Logging;

namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// The ingest pipeline of ADR-0003: one sequential stage validates, stamps the watermarks (BR-LB-11) and routes each
/// message to a bounded partition channel by plate hash; one worker per partition owns a <see cref="VehicleTracker"/>
/// and applies results to the shared boards (camera board first, then the global board, never both locked at once).
/// Time comes only from the stamps and from <see cref="TimeProvider"/>; there is no static state.
/// </summary>
public sealed partial class IngestPipeline : IIngestSink
{
    private const long SweepIntervalTicks = TimeSpan.TicksPerSecond;
    private const long LogThrottleTicks = TimeSpan.TicksPerMinute;

    private readonly ProcessorSettings _settings;
    private readonly BoardSet _boards;
    private readonly MeasurementValidator _validator;
    private readonly IIngestMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestPipeline> _logger;
    private readonly WatermarkClock _clock = new();
    private readonly LatencyWindow _latency;
    private readonly Partition[] _partitions;
    private readonly long[] _lastWarnedTicks = new long[Enum.GetValues<RejectReason>().Length];
    private readonly Lock _idleLock = new();
    private TaskCompletionSource _idle = CompletedSource();
    private int _pending;
    private long _lastReceivedTicks;
    private long _lastLagWarningTicks;
    private volatile bool _running;

    public IngestPipeline(
        ProcessorSettings settings,
        BoardSet boards,
        MeasurementValidator validator,
        IIngestMetrics metrics,
        TimeProvider timeProvider,
        ILogger<IngestPipeline> logger)
    {
        _settings = settings;
        _boards = boards;
        _validator = validator;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _logger = logger;
        _latency = new LatencyWindow(timeProvider);
        _partitions = Enumerable.Range(0, settings.PartitionCount)
            .Select(index => new Partition(index, settings.IngestChannelCapacity, new VehicleTracker(boards.Policy)))
            .ToArray();
    }

    /// <summary>Test hooks around the application of one item by the worker of the given partition.</summary>
    internal Func<int, Task>? BeforeApply { get; set; }

    internal Action<int>? AfterApply { get; set; }

    public bool IsRunning => _running;

    public IngestStatus Status
    {
        get
        {
            var now = _timeProvider.GetUtcNow();
            long newestEvent = _partitions.Max(p => Volatile.Read(ref p.NewestEventTicks));
            long lastReceived = Volatile.Read(ref _lastReceivedTicks);
            DateTimeOffset? newest = newestEvent > 0 ? new DateTimeOffset(newestEvent, TimeSpan.Zero) : null;

            return new IngestStatus(
                _running,
                _partitions.Sum(p => Volatile.Read(ref p.TrackedVehicles)),
                _partitions.Select(p => p.Channel.Reader.Count).ToArray(),
                newest,
                newest is { } n ? now - n : null,
                lastReceived > 0 ? new DateTimeOffset(lastReceived, TimeSpan.Zero) : null,
                _latency.Snapshot());
        }
    }

    public void Start()
    {
        if (_running)
        {
            throw new InvalidOperationException("The pipeline is already running.");
        }

        foreach (var partition in _partitions)
        {
            partition.Worker = Task.Run(() => RunWorkerAsync(partition));
        }

        _running = true;
        LogStarted(_settings.PartitionCount, _settings.IngestChannelCapacity);
    }

    /// <summary>Completes the channels and lets the workers drain until <paramref name="cancellationToken"/> gives up.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        foreach (var partition in _partitions)
        {
            partition.Channel.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(_partitions.Select(p => p.Worker)).WaitAsync(cancellationToken).ConfigureAwait(false);
            LogStopped();
        }
        catch (OperationCanceledException)
        {
            LogDrainTimedOut(_partitions.Sum(p => p.Channel.Reader.Count));
        }
    }

    /// <summary>Completes once every submitted item has been applied; for tests and for a clean shutdown.</summary>
    public Task WaitUntilIdleAsync()
    {
        lock (_idleLock)
        {
            return _idle.Task;
        }
    }

    public async ValueTask SubmitAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        if (!_running)
        {
            throw new InvalidOperationException("The pipeline is not running.");
        }

        _metrics.Received();
        long nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        Volatile.Write(ref _lastReceivedTicks, nowTicks);

        IngestItem item;
        int partitionIndex;

        switch (message)
        {
            case IncomingMeasurement incoming:
                if (!_validator.TryValidate(incoming, out var measurement, out var reason))
                {
                    Reject(reason);
                    return;
                }

                item = IngestItem.ForMeasurement(measurement, _clock.Observe(measurement.CameraId, measurement.TimestampTicks));
                partitionIndex = PlateHash.Partition(measurement.Plate, _partitions.Length);
                break;

            case IncomingHeartbeat heartbeat:
                if (!_settings.HeartbeatsEnabled)
                {
                    _metrics.Ignored();
                    return;
                }

                if (!_validator.TryValidate(heartbeat, out var heartbeatReason))
                {
                    Reject(heartbeatReason);
                    return;
                }

                item = IngestItem.ForHeartbeat(heartbeat.CameraId, _clock.Observe(heartbeat.CameraId, heartbeat.TimestampTicks), heartbeat.TimestampTicks);
                partitionIndex = heartbeat.CameraId % _partitions.Length;
                break;

            default:
                throw new ArgumentException($"Unsupported message type {message.GetType().Name}.", nameof(message));
        }

        WarnOnIngestLag(nowTicks, item.Stamp.GlobalTicks);
        EnterPending();

        try
        {
            await _partitions[partitionIndex].Channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ExitPending();
            throw;
        }
    }

    public void ReportUndecodable(RejectReason reason)
    {
        _metrics.Received();
        Reject(reason);
    }

    private async Task RunWorkerAsync(Partition partition)
    {
        await foreach (var item in partition.Channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (BeforeApply is { } before)
                {
                    await before(partition.Index).ConfigureAwait(false);
                }

                Apply(partition, item);
            }
            catch (Exception exception)
            {
                // A failure for one message must not stop the partition (BR-OPS-01); the boards stay consistent per operation.
                LogProcessingFailure(exception, partition.Index);
            }
            finally
            {
                AfterApply?.Invoke(partition.Index);
                ExitPending();
            }
        }
    }

    private void Apply(Partition partition, in IngestItem item)
    {
        var stamp = item.Stamp;

        if (item.IsHeartbeat)
        {
            _boards.Camera(item.CameraId).AdvanceTo(stamp.CameraTicks);
            _boards.Global.AdvanceTo(stamp.GlobalTicks);
        }
        else
        {
            var result = partition.Tracker.Process(new StampedMeasurement(item.Measurement, stamp));

            if (!result.IsAccepted)
            {
                // A valid but out-of-sequence measurement still moved the watermarks (BR-LB-11), so the boards follow the stamp.
                Reject(RejectReason.OutOfSequence);
                _boards.Camera(item.CameraId).AdvanceTo(stamp.CameraTicks);
                _boards.Global.AdvanceTo(stamp.GlobalTicks);
            }
            else
            {
                _boards.Camera(item.CameraId).Update(stamp.CameraTicks, result.CameraEntry);

                if (result.GlobalEntry is { } globalEntry)
                {
                    _boards.Global.Update(stamp.GlobalTicks, globalEntry);
                }
                else
                {
                    _boards.Global.AdvanceTo(stamp.GlobalTicks);
                }

                _metrics.Accepted();
            }
        }

        if (partition.LastSweepTicks is null || stamp.GlobalTicks - partition.LastSweepTicks >= SweepIntervalTicks)
        {
            partition.Tracker.Sweep(stamp.GlobalTicks);
            partition.LastSweepTicks = stamp.GlobalTicks;
        }

        Volatile.Write(ref partition.TrackedVehicles, partition.Tracker.Count);
        Volatile.Write(ref partition.NewestEventTicks, stamp.GlobalTicks);

        // Latency of this message: when it was applied minus its own event time (not the watermark), never negative (clock skew).
        long latencyTicks = Math.Max(0, _timeProvider.GetUtcNow().UtcTicks - item.EventTicks);
        _latency.Record(latencyTicks);
        _metrics.Latency(TimeSpan.FromTicks(latencyTicks));
    }

    private void Reject(RejectReason reason)
    {
        _metrics.Rejected(reason);

        // The first rejection per reason per minute is a warning, the rest are debug, so a flood cannot flood the log (design section 7).
        long nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        ref long lastWarned = ref _lastWarnedTicks[(int)reason];
        long previous = Volatile.Read(ref lastWarned);

        if (nowTicks - previous >= LogThrottleTicks && Interlocked.CompareExchange(ref lastWarned, nowTicks, previous) == previous)
        {
            LogRejectedFirst(reason);
        }
        else
        {
            LogRejected(reason);
        }
    }

    private void WarnOnIngestLag(long nowTicks, long eventTicks)
    {
        long lagTicks = nowTicks - eventTicks;

        if (lagTicks > _settings.IngestLagWarning.Ticks && nowTicks - _lastLagWarningTicks >= LogThrottleTicks)
        {
            _lastLagWarningTicks = nowTicks;
            LogIngestLag(TimeSpan.FromTicks(lagTicks));
        }
    }

    private void EnterPending()
    {
        lock (_idleLock)
        {
            if (_pending++ == 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void ExitPending()
    {
        lock (_idleLock)
        {
            if (--_pending == 0)
            {
                _idle.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Ingest pipeline started with {PartitionCount} partitions of capacity {ChannelCapacity}")]
    private partial void LogStarted(int partitionCount, int channelCapacity);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Ingest pipeline stopped, all partitions drained")]
    private partial void LogStopped();

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Ingest pipeline stopped before draining; {QueuedItems} queued items were lost")]
    private partial void LogDrainTimedOut(int queuedItems);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Message rejected: {Reason} (further rejections for this reason are logged at Debug for a minute)")]
    private partial void LogRejectedFirst(RejectReason reason);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Debug, Message = "Message rejected: {Reason}")]
    private partial void LogRejected(RejectReason reason);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "Ingest lag is {Lag}; the processor is behind the event time or the clocks disagree")]
    private partial void LogIngestLag(TimeSpan lag);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Error, Message = "Processing a message failed in partition {Partition}")]
    private partial void LogProcessingFailure(Exception exception, int partition);

    /// <summary>One queued message: a stamped measurement or a stamped heartbeat.</summary>
    private readonly record struct IngestItem(bool IsHeartbeat, Measurement Measurement, int CameraId, Stamp Stamp, long EventTicks)
    {
        public static IngestItem ForMeasurement(Measurement measurement, Stamp stamp)
        {
            return new IngestItem(false, measurement, measurement.CameraId, stamp, measurement.TimestampTicks);
        }

        public static IngestItem ForHeartbeat(int cameraId, Stamp stamp, long eventTicks)
        {
            return new IngestItem(true, default, cameraId, stamp, eventTicks);
        }
    }

    private sealed class Partition
    {
        // Written by the owning worker, read by Status; hence Volatile access instead of properties.
#pragma warning disable CA1051 // Do not declare visible instance fields: Volatile.Read/Write need fields, and the class is private.
        public int TrackedVehicles;
        public long NewestEventTicks;
#pragma warning restore CA1051

        public Partition(int index, int capacity, VehicleTracker tracker)
        {
            Index = index;
            Tracker = tracker;
            Channel = System.Threading.Channels.Channel.CreateBounded<IngestItem>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        public int Index { get; }

        public VehicleTracker Tracker { get; }

        public Channel<IngestItem> Channel { get; }

        public Task Worker { get; set; } = Task.CompletedTask;

        public long? LastSweepTicks { get; set; }
    }
}
