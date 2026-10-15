using HighwaySpeedEnforcement.Generator;
using HighwaySpeedEnforcement.Generator.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

public sealed class SimulationRunnerTests : IAsyncDisposable
{
    private const int SummaryEventId = 3004;
    private const int OverrunEventId = 3003;
    private const int AbandonedEventId = 3008;

    private readonly FakeTimeProvider _time = new();
    private readonly CallLog _log = new();
    private readonly FakePublisher _publisher;
    private readonly FakeStore _store;
    private readonly FakeLifetime _lifetime = new();
    private readonly FakeLogger<SimulationRunner> _logger;
    private readonly RunnerStatus _status = new();
    private RuntimeControl? _control;
    private SimulationRunner? _runner;

    public SimulationRunnerTests()
    {
        _publisher = new FakePublisher(_log);
        _store = new FakeStore(_log);
        // Log lines enter the ordered call log too, so a test can await a log line as the observable effect of a tick.
        _logger = new FakeLogger<SimulationRunner>(new FakeLogCollector(Options.Create(new FakeLogCollectorOptions { OutputSink = line => _log.Add($"log {line}") })));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_runner is not null)
        {
            _publisher.Release();
            await _runner.StopAsync(CancellationToken.None);
            _runner.Dispose();
        }
    }

    [Fact]
    [Trait("BR", "BR-SIM-02")]
    public async Task Runner_ticks_once_at_start_and_then_once_per_interval()
    {
        await StartAsync(new GeneratorSettings { Seed = 1 });

        await _log.WhenCallsAsync(2, Token);
        Assert.Equal([1L], _publisher.Published.Select(tick => tick.TickNumber));
        Assert.Equal(_time.GetUtcNow().UtcTicks, _publisher.Published[0].TimestampTicks);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);
        Assert.Equal(_time.GetUtcNow().UtcTicks, _publisher.Published[1].TimestampTicks);

        _time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(2, _publisher.Started.Count);

        _time.Advance(TimeSpan.FromMilliseconds(500));
        await WhenPublishedAsync(3);
        Assert.Equal([1L, 2L, 3L], _publisher.Published.Select(tick => tick.TickNumber));
        Assert.Equal(100, _publisher.Published[0].Measurements.Count);
        Assert.Equal(300, _publisher.Published[2].Measurements.Count);
    }

    [Fact]
    [Trait("BR", "BR-SIM-02")]
    public async Task Runner_does_not_start_a_tick_while_the_previous_one_is_being_published_and_catches_up_once()
    {
        await StartAsync(new GeneratorSettings { Seed = 1 });
        await WhenPublishedAsync(1);
        _publisher.Block();

        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenStartedAsync(2);

        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(2, _publisher.Started.Count);
        Assert.Single(_publisher.Published);

        _publisher.Release();
        await WhenPublishedAsync(3);
        Assert.Equal(3, _publisher.Started.Count);

        _time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(3, _publisher.Started.Count);

        _time.Advance(TimeSpan.FromMilliseconds(500));
        await WhenPublishedAsync(4);
        Assert.Equal(4, _publisher.Started.Count);
    }

    [Fact]
    public async Task Runner_logs_a_warning_when_a_tick_takes_longer_than_the_interval()
    {
        _publisher.Block();
        await StartAsync(new GeneratorSettings { Seed = 1 });
        await WhenStartedAsync(1);

        _time.Advance(TimeSpan.FromMilliseconds(1_500));
        _publisher.Release();
        await WhenAsync(() => _log.Calls.Any(call => call.StartsWith("log ", StringComparison.Ordinal) && call.Contains("longer than the interval", StringComparison.Ordinal)));

        var warning = Assert.Single(_logger.Collector.GetSnapshot(), record => record.Id.Id == OverrunEventId);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("00:00:01.5000000", warning.Message, StringComparison.Ordinal);
        Assert.Contains("100 messages", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_stops_the_host_after_max_ticks_with_everything_published()
    {
        await StartAsync(new GeneratorSettings { Seed = 1, MaxTicks = 3 });
        await WhenPublishedAsync(1);
        Assert.False(_lifetime.StopRequested);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);
        Assert.False(_lifetime.StopRequested);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(3);
        await _runner!.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.True(_lifetime.StopRequested);
        Assert.Equal(3, _publisher.Published.Count);

        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(3, _publisher.Started.Count);
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public async Task Runner_saves_the_plate_state_before_publishing_the_same_tick()
    {
        await StartAsync(new GeneratorSettings { Seed = null, MaxTicks = 2 });
        await WhenPublishedAsync(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);

        Assert.Equal(
            ["load", "save 0", "save 100", "publish-start 1", "publish-done 1", "save 200", "publish-start 2", "publish-done 2"],
            _log.Calls.Where(call => !call.StartsWith("log ", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public async Task Runner_with_a_seed_never_touches_the_store_and_is_reproducible()
    {
        await StartAsync(new GeneratorSettings { Seed = 42, MaxTicks = 2 });
        await WhenPublishedAsync(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);
        var firstPlates = _publisher.Published.SelectMany(tick => tick.Measurements).Select(m => (m.NumberPlate, m.CameraId, m.CurrentSpeed)).ToArray();

        Assert.Equal(0, _store.LoadCalls);
        Assert.Equal(0, _store.SaveCalls);

        var otherLog = new CallLog();
        var otherPublisher = new FakePublisher(otherLog);
        var otherTime = new FakeTimeProvider();
        using var other = Create(new GeneratorSettings { Seed = 42, MaxTicks = 2 }, otherPublisher, new FakeStore(otherLog), otherTime);
        await other.StartAsync(Token);
        await otherLog.WhenCallsAsync(2, Token);
        otherTime.Advance(TimeSpan.FromSeconds(1));
        await otherLog.WhenCallsAsync(4, Token);
        await other.StopAsync(Token);
        var otherPlates = otherPublisher.Published.SelectMany(tick => tick.Measurements).Select(m => (m.NumberPlate, m.CameraId, m.CurrentSpeed)).ToArray();

        Assert.Equal(300, firstPlates.Length);
        Assert.Equal(firstPlates, otherPlates);
    }

    [Fact]
    [Trait("AC", "AC-03")]
    [Trait("BR", "BR-SIM-03")]
    public async Task Runner_without_a_seed_resumes_the_stored_state_so_a_second_run_repeats_no_plate()
    {
        await StartAsync(new GeneratorSettings { Seed = null, MaxTicks = 3 });
        await WhenPublishedAsync(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(3);
        await _runner!.StopAsync(Token);
        var firstRunPlates = _publisher.Published.SelectMany(tick => tick.Measurements).Select(m => m.NumberPlate).ToHashSet(StringComparer.Ordinal);
        var savedState = _store.Stored;

        Assert.Equal(300, firstRunPlates.Count);
        Assert.NotNull(savedState);
        Assert.Equal(300, savedState.Value.NextIndex);

        var secondLog = new CallLog();
        var secondPublisher = new FakePublisher(secondLog);
        var secondStore = new FakeStore(secondLog) { Stored = savedState };
        var secondTime = new FakeTimeProvider();
        using var second = Create(new GeneratorSettings { Seed = null, MaxTicks = 3 }, secondPublisher, secondStore, secondTime);
        await second.StartAsync(Token);
        await secondLog.WhenCallsAsync(4, Token);
        secondTime.Advance(TimeSpan.FromSeconds(1));
        await secondLog.WhenCallsAsync(7, Token);
        secondTime.Advance(TimeSpan.FromSeconds(1));
        await secondLog.WhenCallsAsync(10, Token);
        await second.StopAsync(Token);
        var secondRunPlates = secondPublisher.Published.SelectMany(tick => tick.Measurements).Select(m => m.NumberPlate).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(300, secondRunPlates.Count);
        Assert.Empty(firstRunPlates.Intersect(secondRunPlates));
        Assert.Equal(1, secondStore.LoadCalls);
        Assert.Equal(600, secondStore.Stored!.Value.NextIndex);
        Assert.Equal(savedState.Value.A, secondStore.Stored.Value.A);
        Assert.Equal(savedState.Value.B, secondStore.Stored.Value.B);
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public async Task Runner_without_a_seed_and_without_a_stored_state_creates_and_saves_a_new_state()
    {
        Assert.Null(_store.Stored);

        await StartAsync(new GeneratorSettings { Seed = null });
        await WhenPublishedAsync(1);

        Assert.Equal(1, _store.LoadCalls);
        Assert.Equal(2, _store.SaveCalls);
        Assert.Equal(100, _store.Stored!.Value.NextIndex);
        Assert.True(PlatePermutation.IsValidState(PlateFormat.Size, _store.Stored.Value));
    }

    [Fact]
    public async Task Runner_stop_request_during_a_blocked_publish_completes_the_tick_and_then_stops()
    {
        await StartAsync(new GeneratorSettings { Seed = 1 });
        await WhenPublishedAsync(1);
        _publisher.Block();
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenStartedAsync(2);

        var stopping = _runner!.StopAsync(Token);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.False(stopping.IsCompleted);
        Assert.Single(_publisher.Published);

        _publisher.Release();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal([1L, 2L], _publisher.Published.Select(tick => tick.TickNumber));
        Assert.Equal(2, _publisher.Started.Count);
        Assert.DoesNotContain(_logger.Collector.GetSnapshot(), record => record.Id.Id == AbandonedEventId);
    }

    [Fact]
    public async Task Runner_abandons_a_publish_that_never_completes_after_the_grace_limit()
    {
        _publisher.Block();
        await StartAsync(new GeneratorSettings { Seed = 1 });
        await WhenStartedAsync(1);

        var stopping = _runner!.StopAsync(Token);
        _time.Advance(SimulationRunner.PublishGraceLimit - TimeSpan.FromMilliseconds(1));
        Assert.False(stopping.IsCompleted);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Empty(_publisher.Published);
        var warning = Assert.Single(_logger.Collector.GetSnapshot(), record => record.Id.Id == AbandonedEventId);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task Runner_logs_a_summary_every_ten_ticks()
    {
        await StartAsync(new GeneratorSettings { Seed = 1 });
        await WhenPublishedAsync(1);

        for (int tick = 2; tick <= 20; tick++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await WhenPublishedAsync(tick);
        }

        var summaries = _logger.Collector.GetSnapshot().Where(record => record.Id.Id == SummaryEventId).ToArray();

        Assert.Equal(2, summaries.Length);
        Assert.All(summaries, summary => Assert.Equal(LogLevel.Information, summary.Level));
        Assert.Contains("10 ticks", summaries[0].Message, StringComparison.Ordinal);
        Assert.Contains("5500 messages", summaries[0].Message, StringComparison.Ordinal);
        Assert.Contains("1000 plates drawn", summaries[0].Message, StringComparison.Ordinal);
        Assert.Contains("20 ticks", summaries[1].Message, StringComparison.Ordinal);
        Assert.Contains("15500 messages", summaries[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_logs_the_start_up_summary_with_the_seed_and_mode()
    {
        await StartAsync(new GeneratorSettings { Seed = 42, DisabledCameras = [7] });
        await WhenPublishedAsync(1);

        var started = Assert.Single(_logger.Collector.GetSnapshot(), record => record.Id.Id == 3001);
        Assert.All(
            new[] { "Seed=42", "SeedConfigured=True", "DisabledCameras=[7]", "PublisherMode=PerCamera", "StatePath=not used" },
            part => Assert.True(started.Message.Contains(part, StringComparison.Ordinal), $"'{part}' missing in: {started.Message}"));
    }

    [Fact]
    [Trait("BR", "BR-SIM-02")]
    public async Task Runner_applies_an_operator_change_at_the_next_tick_and_not_before()
    {
        await StartAsync(new GeneratorSettings { Seed = 1 });
        await WhenPublishedAsync(1);

        _control!.TogglePause();
        _control.IncreaseVehicles();
        _control.ToggleHeartbeats();
        _control.ToggleCamera(7);
        _time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Single(_publisher.Published);
        Assert.DoesNotContain(_logger.Collector.GetSnapshot(), record => record.Message.StartsWith("operator:", StringComparison.Ordinal));

        _time.Advance(TimeSpan.FromMilliseconds(500));
        await WhenPublishedAsync(2);
        var paused = _publisher.Published[1];
        Assert.Equal(0, paused.Spawned);
        Assert.Equal(100, paused.Measurements.Count);
        Assert.All(paused.Measurements, m => Assert.Equal(2, m.CameraId));
        Assert.Equal([1, 3, 4, 5, 6, 8, 9, 10], paused.HeartbeatCameras);

        _control.TogglePause();
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(3);
        var resumed = _publisher.Published[2];
        Assert.Equal(200, resumed.Spawned);
        Assert.Equal(300, resumed.Measurements.Count);
        Assert.Equal(200, resumed.Measurements.Count(m => m.CameraId == 1));
    }

    [Fact]
    public async Task Runner_logs_each_operator_change_once_with_the_old_and_new_value()
    {
        await StartAsync(new GeneratorSettings { Seed = 1, DisabledCameras = [3] });
        await WhenPublishedAsync(1);

        _control!.TogglePause();
        _control.IncreaseVehicles();
        _control.IncreaseVehicles();
        _control.ToggleHeartbeats();
        _control.ToggleCamera(7);
        _control.ToggleCamera(3);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(3);

        var operatorLines = _logger.Collector.GetSnapshot().Where(record => record.Message.StartsWith("operator:", StringComparison.Ordinal)).ToArray();

        Assert.All(operatorLines, record => Assert.Equal(LogLevel.Information, record.Level));
        Assert.Equal(
            [
                "operator: traffic RUNNING -> PAUSED",
                "operator: vehicles per tick 100 -> 300",
                "operator: heartbeats off -> on",
                "operator: cameras off [3] -> [7]",
            ],
            operatorLines.Select(record => record.Message));
    }

    [Fact]
    public async Task Runner_reports_the_seed_and_each_completed_tick_through_the_status()
    {
        var changes = 0;
        _status.Changed += () => changes++;

        await StartAsync(new GeneratorSettings { Seed = 42 });
        await WhenPublishedAsync(1);
        Assert.Equal(new RunnerStatusValues(1, 100, TimeSpan.Zero, 42), _status.Current);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);
        Assert.Equal(new RunnerStatusValues(2, 300, TimeSpan.Zero, 42), _status.Current);
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task Runner_without_a_configured_seed_reports_no_seed()
    {
        await StartAsync(new GeneratorSettings { Seed = null });
        await WhenPublishedAsync(1);

        Assert.Null(_status.Current.Seed);
        Assert.Equal(1, _status.Current.Tick);
    }

    [Fact]
    public async Task Runner_logs_nothing_when_a_change_is_reverted_before_the_tick()
    {
        await StartAsync(new GeneratorSettings { Seed = 1 });
        await WhenPublishedAsync(1);

        _control!.TogglePause();
        _control.TogglePause();
        _control.ToggleCamera(5);
        _control.ToggleCamera(5);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WhenPublishedAsync(2);

        Assert.DoesNotContain(_logger.Collector.GetSnapshot(), record => record.Message.StartsWith("operator:", StringComparison.Ordinal));
        Assert.Equal(100, _publisher.Published[1].Spawned);
    }

    [Fact]
    [Trait("BR", "BR-SIM-02")]
    public async Task Runner_without_operator_changes_publishes_exactly_what_the_plain_simulation_produces()
    {
        var settings = new GeneratorSettings { Seed = 7, DisabledCameras = [2], Heartbeats = new GeneratorSettings.HeartbeatSettings { Enabled = true }, SpawnUntilTick = 2 };
        var reference = SimulationStartup.Create(settings, new FakeStore(new CallLog()), 0).Simulation;
        var expected = new List<TickOutput>();

        await StartAsync(settings);
        await WhenPublishedAsync(1);
        expected.Add(reference.Tick(_time.GetUtcNow().UtcTicks));

        for (int tick = 2; tick <= 6; tick++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await WhenPublishedAsync(tick);
            expected.Add(reference.Tick(_time.GetUtcNow().UtcTicks));
        }

        Assert.Equal(expected.Select(tick => tick with { Measurements = [], HeartbeatCameras = [] }), _publisher.Published.Select(tick => tick with { Measurements = [], HeartbeatCameras = [] }));
        Assert.Equal(expected.SelectMany(tick => tick.Measurements), _publisher.Published.SelectMany(tick => tick.Measurements));
        Assert.Equal(expected.SelectMany(tick => tick.HeartbeatCameras), _publisher.Published.SelectMany(tick => tick.HeartbeatCameras));
        Assert.DoesNotContain(_logger.Collector.GetSnapshot(), record => record.Message.StartsWith("operator:", StringComparison.Ordinal));
    }

    private async Task StartAsync(GeneratorSettings settings)
    {
        _control = new RuntimeControl(settings.ToSimulationOptions());
        _runner = new SimulationRunner(settings, _control, _status, _publisher, _store, _time, _lifetime, _logger);
        await _runner.StartAsync(Token);
    }

    private static SimulationRunner Create(GeneratorSettings settings, FakePublisher publisher, FakeStore store, FakeTimeProvider time)
    {
        return new SimulationRunner(settings, new RuntimeControl(settings.ToSimulationOptions()), new RunnerStatus(), publisher, store, time, new FakeLifetime(), new FakeLogger<SimulationRunner>());
    }

    private Task WhenPublishedAsync(int count)
    {
        return WhenAsync(() => _publisher.Published.Count >= count);
    }

    private Task WhenStartedAsync(int count)
    {
        return WhenAsync(() => _publisher.Started.Count >= count);
    }

    private async Task WhenAsync(Func<bool> condition)
    {
        int calls = _log.Calls.Count;

        while (!condition())
        {
            calls++;
            await _log.WhenCallsAsync(calls, Token);
        }
    }
}
