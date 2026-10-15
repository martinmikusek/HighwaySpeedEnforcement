using System.Globalization;
using System.Text;
using HighwaySpeedEnforcement.E2E.Tests.Support;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.E2E.Tests.Processor;

/// <summary>
/// Step 19 of the processor plan: the assignment's load (100 new vehicles per second, 1,000 measurements per second) for
/// <c>HSE_LOAD_SECONDS</c> (default 300) followed by a 10x burst, against the processor container. Thresholds are those of
/// <c>docs/testing-strategy.md</c>. Runs only on request (the project excludes <c>Category=Load</c> by default).
/// The ingest lag is sampled right after each tick was published, so it measures how far the processor is behind the
/// head of the stream (publish time of the tick plus processing), not the idle gap inside a tick.
/// </summary>
[Collection(PipelineCollection.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Load")]
public sealed class LoadTests
{
    private const int VehiclesPerTick = 100;
    private const int BurstFactor = 10;
    private const int WarmUpTicks = 20;
    private const int RecoveryTicks = 10;

    private readonly PipelineStack _stack;

    public LoadTests(PipelineStack stack)
    {
        _stack = stack;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int SteadyTicks =>
        int.TryParse(Environment.GetEnvironmentVariable("HSE_LOAD_SECONDS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : 300;

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    [Trait("BR", "BR-ING-03")]
    public async Task Steady_load_and_a_burst_keep_the_lag_low_the_vehicles_bounded_and_the_memory_flat()
    {
        await using var processor = await _stack.StartProcessorAsync(ProcessorRun.Default, Ct);
        await using var publisher = await TestPublisher.ConnectAsync(_stack.Broker, TelemetryContract.Topic, Ct);
        using var memory = new ContainerMemory();
        var generator = new SimulatedTraffic();
        var samples = new List<Sample>();
        using var ticks = new PeriodicTimer(TimeSpan.FromSeconds(1));

        for (var tick = 0; tick < SteadyTicks; tick++)
        {
            await ticks.WaitForNextTickAsync(Ct);
            await PublishTickAsync(publisher, generator.NextTick(VehiclesPerTick), Ct);
            samples.Add(await SampleAsync(processor, memory, tick < WarmUpTicks ? Phase.WarmUp : Phase.Steady, Ct));
        }

        await ticks.WaitForNextTickAsync(Ct);
        var burstStarted = DateTimeOffset.UtcNow;
        await PublishTickAsync(publisher, generator.NextTick(VehiclesPerTick * BurstFactor), Ct);
        var burstDuration = DateTimeOffset.UtcNow - burstStarted;
        samples.Add(await SampleAsync(processor, memory, Phase.Burst, Ct));

        for (var tick = 0; tick < RecoveryTicks; tick++)
        {
            await ticks.WaitForNextTickAsync(Ct);
            await PublishTickAsync(publisher, generator.NextTick(VehiclesPerTick), Ct);
            samples.Add(await SampleAsync(processor, memory, Phase.Recovery, Ct));
        }

        await processor.WaitUntilAppliedAsync(generator.Sent, Ct);
        var counters = (await processor.GetStatusAsync(Ct))["counters"]!;

        var steady = samples.Where(s => s.Phase == Phase.Steady).ToArray();
        var recovery = samples.Where(s => s.Phase == Phase.Recovery).ToArray();
        var lagP99 = Percentile(steady.Select(s => s.LagSeconds), 0.99);
        var firstMinuteMemory = steady.Take(60).Average(s => s.MemoryBytes);
        var lastMinuteMemory = steady.TakeLast(60).Average(s => s.MemoryBytes);
        var recovered = recovery.FirstOrDefault(s => s.LagSeconds < 1);

        var report = Report(samples, steady, burstDuration, lagP99, firstMinuteMemory, lastMinuteMemory, recovered, counters.ToJsonString());
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        // A passing test prints nothing on the console, so the measurements are also written next to the test binaries.
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "load-report.txt"), report, Ct);

        Assert.Equal(generator.Sent, (long)counters["received"]!);
        Assert.Equal(generator.Sent, (long)counters["accepted"]!);
        Assert.Equal(0, (long)counters["rejectedTotal"]!);
        Assert.True(lagP99 < 1, $"ingest lag p99 {lagP99:F3} s must stay below 1 s");
        Assert.All(steady, s => Assert.InRange(s.TrackedVehicles, 800, 1000));
        Assert.True(lastMinuteMemory <= firstMinuteMemory * 1.25 + 20 * 1024 * 1024, $"memory grew from {firstMinuteMemory / 1048576:F1} MiB to {lastMinuteMemory / 1048576:F1} MiB");
        Assert.True(recovered is not null, "the lag did not fall below 1 s within 10 s after the burst");
        Assert.True(samples[^1].LagSeconds < 1, $"final lag {samples[^1].LagSeconds:F3} s");
    }

    /// <summary>Publishes a tick in batches of concurrent QoS 1 publishes; each batch completes before the next, so a plate's order holds.</summary>
    private static async Task PublishTickAsync(TestPublisher publisher, IReadOnlyList<Telemetry> tick, CancellationToken cancellationToken)
    {
        foreach (var batch in tick.Chunk(100))
        {
            await Task.WhenAll(batch.Select(m => publisher.PublishMeasurementAsync(m.Plate, m.Camera, m.Ticks, m.Speed, cancellationToken)));
        }
    }

    private static async Task<Sample> SampleAsync(ProcessorContainer processor, ContainerMemory memory, Phase phase, CancellationToken cancellationToken)
    {
        var ingest = (await processor.GetStatusAsync(cancellationToken))["ingest"]!;
        var bytes = await memory.UsageBytesAsync(processor.Id, cancellationToken);
        return new Sample(phase, (double?)ingest["ingestLagSeconds"] ?? 0, (int)ingest["trackedVehicles"]!, (int)ingest["bufferDepth"]!, bytes);
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? double.NaN : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
    }

    private static string Report(IReadOnlyList<Sample> samples, IReadOnlyList<Sample> steady, TimeSpan burstDuration, double lagP99, double firstMinuteMemory, double lastMinuteMemory, Sample? recovered, string counters)
    {
        var burst = samples.Single(s => s.Phase == Phase.Burst);
        var recovery = samples.Where(s => s.Phase == Phase.Recovery).ToArray();
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"ticks: {samples.Count}, steady (after warm-up): {steady.Count}");
        report.AppendLine(CultureInfo.InvariantCulture, $"counters: {counters}");
        report.AppendLine(CultureInfo.InvariantCulture, $"ingest lag after each tick (s): p50 {Percentile(steady.Select(s => s.LagSeconds), 0.5):F3}, p99 {lagP99:F3}, max {steady.Max(s => s.LagSeconds):F3}");
        report.AppendLine(CultureInfo.InvariantCulture, $"tracked vehicles: min {steady.Min(s => s.TrackedVehicles)}, max {steady.Max(s => s.TrackedVehicles)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"buffer depth after a tick: max {steady.Max(s => s.BufferDepth)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"memory MiB: first minute {firstMinuteMemory / 1048576:F1}, last minute {lastMinuteMemory / 1048576:F1}, max {samples.Max(s => s.MemoryBytes) / 1048576.0:F1}");
        report.AppendLine(CultureInfo.InvariantCulture, $"burst of {VehiclesPerTick * BurstFactor} new vehicles: published in {burstDuration.TotalSeconds:F2} s, lag right after {burst.LagSeconds:F3} s, buffer depth {burst.BufferDepth}, lag per recovery tick: {string.Join(" ", recovery.Select(s => s.LagSeconds.ToString("F2", CultureInfo.InvariantCulture)))}, recovered: {recovered is not null}");
        return report.ToString();
    }

    private enum Phase
    {
        WarmUp,
        Steady,
        Burst,
        Recovery,
    }

    private sealed record Sample(Phase Phase, double LagSeconds, int TrackedVehicles, int BufferDepth, long MemoryBytes);

    /// <summary>The generator's tick (BR-SIM-02) in test form: spawn, advance every active vehicle by one camera, drop the completed ones. Each measurement is stamped when it is created (BR-TEL-02).</summary>
    private sealed class SimulatedTraffic
    {
        private readonly List<(string Plate, int Camera)> _active = [];
        private long _nextPlate;

        public long Sent { get; private set; }

        public IReadOnlyList<Telemetry> NextTick(int newVehicles)
        {
            for (var i = 0; i < newVehicles; i++)
            {
                _active.Add(($"L{_nextPlate++:D8}", 0));
            }

            var tick = new Telemetry[_active.Count];

            for (var i = 0; i < _active.Count; i++)
            {
                var (plate, camera) = _active[i];
                _active[i] = (plate, camera + 1);
                tick[i] = new Telemetry(plate, camera + 1, DateTime.UtcNow.Ticks, 110f + (plate.GetHashCode() & 31));
            }

            _active.RemoveAll(v => v.Camera == 10);
            Sent += tick.Length;
            return tick;
        }
    }
}
