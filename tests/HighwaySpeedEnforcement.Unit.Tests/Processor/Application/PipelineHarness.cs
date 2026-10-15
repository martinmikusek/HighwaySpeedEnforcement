using System.Collections.Concurrent;
using HighwaySpeedEnforcement.Processor.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Application;

/// <summary>A started pipeline with a fake clock, recording metrics and an optional recording logger.</summary>
internal sealed class PipelineHarness : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Event time base one hour in the past, so that test timestamps are never ahead of the clock.</summary>
    public static readonly long BaseTicks = Now.UtcTicks - TimeSpan.FromHours(1).Ticks;

    public static readonly long Second = TimeSpan.FromSeconds(1).Ticks;

    public PipelineHarness(ProcessorSettings? settings = null, bool recordLogs = false)
    {
        Settings = settings ?? new ProcessorSettings();
        Time = new FakeTimeProvider(Now);
        Boards = new BoardSet(Settings.ToLivenessPolicy());
        Logger = recordLogs ? new RecordingLogger() : null;
        Pipeline = new IngestPipeline(
            Settings,
            Boards,
            new MeasurementValidator(Settings, Time),
            Metrics,
            Time,
            Logger ?? (ILogger<IngestPipeline>)NullLogger<IngestPipeline>.Instance);
        Pipeline.Start();
    }

    public ProcessorSettings Settings { get; }

    public FakeTimeProvider Time { get; }

    public BoardSet Boards { get; }

    public RecordingMetrics Metrics { get; } = new();

    public RecordingLogger? Logger { get; }

    public IngestPipeline Pipeline { get; }

    public static long At(double seconds)
    {
        return BaseTicks + (long)(seconds * Second);
    }

    public static IncomingMeasurement Measurement(string plate, int camera, double seconds, float speed = 130)
    {
        return new IncomingMeasurement(plate, At(seconds), speed, camera);
    }

    public async Task SubmitAsync(params IncomingMessage[] messages)
    {
        foreach (var message in messages)
        {
            await Pipeline.SubmitAsync(message, TestContext.Current.CancellationToken);
        }
    }

    public async Task SubmitAndDrainAsync(params IncomingMessage[] messages)
    {
        await SubmitAsync(messages);
        await Pipeline.WaitUntilIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
    }

    public IEnumerable<string> CameraPlates(int camera)
    {
        return Boards.Camera(camera).Top(10).Entries.Select(e => e.Entry.Plate);
    }

    public IEnumerable<string> GlobalPlates()
    {
        return Boards.Global.Top(10).Entries.Select(e => e.Entry.Plate);
    }

    public async ValueTask DisposeAsync()
    {
        // A failed assertion may leave a worker blocked on a test gate; the stop must then give up instead of hanging the run.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await Pipeline.StopAsync(timeout.Token);
    }

    internal sealed class RecordingMetrics : IIngestMetrics
    {
        private readonly ConcurrentDictionary<RejectReason, int> _rejected = new();
        private readonly ConcurrentQueue<TimeSpan> _latencies = new();
        private int _received;
        private int _accepted;
        private int _ignored;

        public int ReceivedCount => Volatile.Read(ref _received);

        public int AcceptedCount => Volatile.Read(ref _accepted);

        public int IgnoredCount => Volatile.Read(ref _ignored);

        public IReadOnlyList<TimeSpan> Latencies => _latencies.ToArray();

        public int RejectedCount(RejectReason reason)
        {
            return _rejected.GetValueOrDefault(reason);
        }

        public int RejectedTotal => _rejected.Values.Sum();

        public void Received()
        {
            Interlocked.Increment(ref _received);
        }

        public void Accepted()
        {
            Interlocked.Increment(ref _accepted);
        }

        public void Rejected(RejectReason reason)
        {
            _rejected.AddOrUpdate(reason, 1, (_, count) => count + 1);
        }

        public void Ignored()
        {
            Interlocked.Increment(ref _ignored);
        }

        public void Latency(TimeSpan latency)
        {
            _latencies.Enqueue(latency);
        }
    }

    internal sealed class RecordingLogger : ILogger<IngestPipeline>
    {
        public ConcurrentQueue<(LogLevel Level, EventId EventId, string Message)> Records { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Records.Enqueue((logLevel, eventId, formatter(state, exception)));
        }
    }
}
