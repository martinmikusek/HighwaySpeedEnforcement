using HighwaySpeedEnforcement.Generator.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HighwaySpeedEnforcement.Generator;

/// <summary>
/// The tick loop of design sections 2 and 5: one tick per second on the injected <see cref="TimeProvider"/>, tick 1 at start,
/// each tick simulated, its plate state saved and then published before the next one starts. Missed periods are not caught
/// up. Stops the host after <see cref="GeneratorSettings.MaxTicks"/>; a stop request lets the tick being published finish,
/// up to a grace limit.
/// </summary>
public sealed partial class SimulationRunner : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan PublishGraceLimit = TimeSpan.FromSeconds(10);

    private const int TicksPerSummary = 10;

    private readonly GeneratorSettings _settings;
    private readonly RuntimeControl _control;
    private readonly RunnerStatus _status;
    private readonly IMeasurementPublisher _publisher;
    private readonly IPlateStateStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SimulationRunner> _logger;
    private readonly SimulationOptions _baselineOptions;

    private RuntimeControlSnapshot _applied;
    private long _ticks;
    private long _messages;
    private long _wrapsSeen;
    private TimeSpan _longestTickSinceSummary;

    public SimulationRunner(
        GeneratorSettings settings,
        RuntimeControl control,
        RunnerStatus status,
        IMeasurementPublisher publisher,
        IPlateStateStore store,
        TimeProvider timeProvider,
        IHostApplicationLifetime lifetime,
        ILogger<SimulationRunner> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baselineOptions = settings.ToSimulationOptions();
        // Changes are reported against the configured values, so a key pressed before tick 1 is logged as well.
        _applied = new RuntimeControlSnapshot(_baselineOptions.TrafficPaused, _baselineOptions.VehiclesPerTick, _baselineOptions.HeartbeatsEnabled, _baselineOptions.DisabledCameras.Order().ToArray());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startup = SimulationStartup.Create(_settings, _store, Random.Shared.NextInt64());
        LogStarted(startup);
        _status.SetSeed(startup.SeedConfigured ? startup.Seed : null);

        using var timer = new PeriodicTimer(TickInterval, _timeProvider);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunTickAsync(startup, stoppingToken).ConfigureAwait(false);

                if (_settings.MaxTicks is { } maxTicks && _ticks >= maxTicks)
                {
                    LogMaxTicksReached(maxTicks);
                    _lifetime.StopApplication();
                    return;
                }

                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A stop request while waiting for the next tick.
        }
        finally
        {
            LogFinalSummary(_ticks, _messages, startup.Simulation.PlatesDrawn);
        }
    }

    private async Task RunTickAsync(SimulationStartup startup, CancellationToken stoppingToken)
    {
        long started = _timeProvider.GetTimestamp();
        var simulation = startup.Simulation;
        var options = ApplyOperatorChanges();
        var output = simulation.Tick(_timeProvider.GetUtcNow().UtcTicks, options);

        // Saved before publishing: a plate that may have reached the broker is then always recorded as used (BR-SIM-03).
        if (startup.StoreInUse)
        {
            _store.Save(simulation.Permutation.State);
        }

        if (simulation.Permutation.WrapCount > _wrapsSeen)
        {
            _wrapsSeen = simulation.Permutation.WrapCount;
            LogPermutationWrapped(output.TickNumber, PlateFormat.Size);
        }

        await PublishAsync(output, stoppingToken).ConfigureAwait(false);

        var duration = _timeProvider.GetElapsedTime(started);
        int messageCount = output.Measurements.Count + output.HeartbeatCameras.Count;
        _ticks = output.TickNumber;
        _messages += messageCount;
        _longestTickSinceSummary = duration > _longestTickSinceSummary ? duration : _longestTickSinceSummary;
        _status.RecordTick(_ticks, _messages, duration);

        if (duration > TickInterval)
        {
            LogTickOverrun(output.TickNumber, duration, messageCount);
        }
        else
        {
            LogTickCompleted(output.TickNumber, duration, output.Measurements.Count, output.HeartbeatCameras.Count, output.Active);
        }

        if (output.TickNumber % TicksPerSummary == 0)
        {
            LogSummary(_ticks, _messages, simulation.PlatesDrawn, _longestTickSinceSummary);
            _longestTickSinceSummary = TimeSpan.Zero;
        }
    }

    /// <summary>Reads the operator's settings once per tick (design section 11) and logs what changed since the last tick.</summary>
    private SimulationOptions ApplyOperatorChanges()
    {
        var snapshot = _control.Snapshot();

        if (snapshot != _applied)
        {
            if (snapshot.TrafficPaused != _applied.TrafficPaused)
            {
                LogOperatorTraffic(TrafficState(_applied.TrafficPaused), TrafficState(snapshot.TrafficPaused));
            }

            if (snapshot.VehiclesPerTick != _applied.VehiclesPerTick)
            {
                LogOperatorVehiclesPerTick(_applied.VehiclesPerTick, snapshot.VehiclesPerTick);
            }

            if (snapshot.HeartbeatsEnabled != _applied.HeartbeatsEnabled)
            {
                LogOperatorHeartbeats(OnOff(_applied.HeartbeatsEnabled), OnOff(snapshot.HeartbeatsEnabled));
            }

            if (!snapshot.DisabledCameras.SequenceEqual(_applied.DisabledCameras))
            {
                LogOperatorCamerasOff(string.Join(",", _applied.DisabledCameras), string.Join(",", snapshot.DisabledCameras));
            }

            _applied = snapshot;
        }

        return snapshot.ToSimulationOptions(_baselineOptions);
    }

    private static string TrafficState(bool paused)
    {
        return paused ? "PAUSED" : "RUNNING";
    }

    private static string OnOff(bool enabled)
    {
        return enabled ? "on" : "off";
    }

    private async Task PublishAsync(TickOutput output, CancellationToken stoppingToken)
    {
        using var abandon = new CancellationTokenSource();
        // Disposed before the source above, so the callback cannot run against a disposed source.
        using var graceTimer = _timeProvider.CreateTimer(static state => ((CancellationTokenSource)state!).Cancel(), abandon, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        using var registration = stoppingToken.Register(() => graceTimer.Change(PublishGraceLimit, Timeout.InfiniteTimeSpan));

        try
        {
            await _publisher.PublishTickAsync(output, abandon.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (abandon.IsCancellationRequested)
        {
            LogTickAbandoned(output.TickNumber, PublishGraceLimit);
        }
    }

    private void LogStarted(SimulationStartup startup)
    {
        LogStarted(
            _settings.VehiclesPerTick,
            _settings.SpawnUntilTick,
            _settings.MaxTicks,
            string.Join(",", _settings.DisabledCameras),
            _settings.Heartbeats.Enabled,
            startup.Seed,
            startup.SeedConfigured,
            startup.StoreInUse ? _settings.StatePath : "not used",
            startup.StateResumed,
            _settings.Mqtt.PublisherMode.ToString(),
            _settings.Mqtt.Host,
            _settings.Mqtt.Port,
            _settings.Mqtt.Topic,
            _settings.Mqtt.RunId);
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Generator started: VehiclesPerTick={VehiclesPerTick} SpawnUntilTick={SpawnUntilTick} MaxTicks={MaxTicks} DisabledCameras=[{DisabledCameras}] Heartbeats={Heartbeats} Seed={Seed} SeedConfigured={SeedConfigured} StatePath={StatePath} StateResumed={StateResumed} PublisherMode={PublisherMode} Broker={Host}:{Port} Topic={Topic} RunId={RunId}")]
    private partial void LogStarted(
        int vehiclesPerTick, long? spawnUntilTick, long? maxTicks, string disabledCameras, bool heartbeats, long seed, bool seedConfigured,
        string statePath, bool stateResumed, string publisherMode, string host, int port, string topic, string runId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "Tick {Tick} completed in {Duration}: {Measurements} measurements, {Heartbeats} heartbeats, {Active} active vehicles")]
    private partial void LogTickCompleted(long tick, TimeSpan duration, int measurements, int heartbeats, int active);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Tick {Tick} took {Duration}, longer than the interval; {Messages} messages")]
    private partial void LogTickOverrun(long tick, TimeSpan duration, int messages);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Summary: {Ticks} ticks, {Messages} messages, {PlatesDrawn} plates drawn, longest tick {LongestTick}")]
    private partial void LogSummary(long ticks, long messages, long platesDrawn, TimeSpan longestTick);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "Generator stopped: {Ticks} ticks, {Messages} messages, {PlatesDrawn} plates drawn")]
    private partial void LogFinalSummary(long ticks, long messages, long platesDrawn);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "Plate permutation wrapped at tick {Tick}: all {Size} plates were used, plates repeat from now on")]
    private partial void LogPermutationWrapped(long tick, long size);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information, Message = "MaxTicks {MaxTicks} reached, stopping")]
    private partial void LogMaxTicksReached(long maxTicks);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning, Message = "Tick {Tick} was still being published {GraceLimit} after the stop request and was abandoned")]
    private partial void LogTickAbandoned(long tick, TimeSpan graceLimit);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "operator: traffic {Old} -> {New}")]
    private partial void LogOperatorTraffic(string old, string @new);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Information, Message = "operator: vehicles per tick {Old} -> {New}")]
    private partial void LogOperatorVehiclesPerTick(int old, int @new);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Information, Message = "operator: heartbeats {Old} -> {New}")]
    private partial void LogOperatorHeartbeats(string old, string @new);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Information, Message = "operator: cameras off [{Old}] -> [{New}]")]
    private partial void LogOperatorCamerasOff(string old, string @new);
}
