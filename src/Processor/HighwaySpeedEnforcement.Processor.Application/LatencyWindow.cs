namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// Average and maximum latency of the messages processed in the last <see cref="LatencyWindow.WindowSeconds"/> seconds (BR-OPS-02).
/// Latency is the wall-clock time at which a message was applied minus its event time, so it does not saw-tooth with the
/// one-second tick of the generator the way the age of the newest event does.
/// </summary>
public sealed record LatencyStats(long Count, TimeSpan Average, TimeSpan Maximum);

/// <summary>Per-second buckets of count, sum and maximum, written by the workers and read by the status; one short lock guards them.</summary>
internal sealed class LatencyWindow
{
    public const int WindowSeconds = 10;

    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private readonly Bucket[] _buckets = new Bucket[WindowSeconds];

    public LatencyWindow(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Record(long latencyTicks)
    {
        long second = CurrentSecond();
        int index = (int)(second % WindowSeconds);

        lock (_lock)
        {
            ref var bucket = ref _buckets[index];

            if (bucket.Second != second)
            {
                bucket = new Bucket { Second = second };
            }

            bucket.Count++;
            bucket.SumTicks += latencyTicks;
            bucket.MaxTicks = Math.Max(bucket.MaxTicks, latencyTicks);
        }
    }

    /// <summary>The statistics of the last <see cref="WindowSeconds"/> seconds, or null when nothing was processed in them.</summary>
    public LatencyStats? Snapshot()
    {
        long second = CurrentSecond();
        long count = 0;
        long sum = 0;
        long max = 0;

        lock (_lock)
        {
            foreach (var bucket in _buckets)
            {
                if (bucket.Count > 0 && second - bucket.Second < WindowSeconds)
                {
                    count += bucket.Count;
                    sum += bucket.SumTicks;
                    max = Math.Max(max, bucket.MaxTicks);
                }
            }
        }

        return count == 0 ? null : new LatencyStats(count, TimeSpan.FromTicks(sum / count), TimeSpan.FromTicks(max));
    }

    private long CurrentSecond()
    {
        return _timeProvider.GetUtcNow().UtcTicks / TimeSpan.TicksPerSecond;
    }

    private struct Bucket
    {
        public long Second;
        public long Count;
        public long SumTicks;
        public long MaxTicks;
    }
}
