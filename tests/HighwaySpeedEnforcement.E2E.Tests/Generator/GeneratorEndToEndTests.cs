using System.Text.Json;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.E2E.Tests.Generator;

/// <summary>Step 12 of the generator plan: the real generator process, a real broker and a recording subscriber (AC-01..03, AC-33, AC-34).</summary>
[Trait("Category", "E2E")]
public sealed class GeneratorEndToEndTests : IClassFixture<GeneratorRunFixture>
{
    private readonly GeneratorRunFixture _run;

    public GeneratorEndToEndTests(GeneratorRunFixture run)
    {
        _run = run;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("AC", "AC-01")]
    [Trait("BR", "BR-SIM-04")]
    public void Shared_run_every_plate_passes_cameras_one_to_ten_in_order_and_never_eleven()
    {
        Assert.Equal(0, _run.Result.ExitCode);
        Assert.All(_run.Messages, message => Assert.Equal(TelemetryContract.MeasurementType, message.Type));
        Assert.All(_run.Messages, message => Assert.InRange(message.CameraId, 1, 10));

        var camerasByPlate = _run.Messages.GroupBy(m => m.NumberPlate!).ToDictionary(g => g.Key, g => g.Select(m => m.CameraId).ToArray());

        Assert.Equal(1_500, camerasByPlate.Count);
        Assert.All(camerasByPlate.Values, cameras => Assert.Equal(Enumerable.Range(1, cameras.Length), cameras));
        Assert.Equal(600, camerasByPlate.Values.Count(cameras => cameras.Length == 10));
    }

    [Fact]
    [Trait("AC", "AC-02")]
    [Trait("BR", "BR-SIM-02")]
    public void Shared_run_steady_state_ticks_carry_a_thousand_messages_a_hundred_per_camera_and_a_hundred_new_plates()
    {
        Assert.Equal(10_500, _run.Messages.Count);

        var ticks = _run.Messages.GroupBy(m => m.TimestampTicks).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();

        Assert.Equal(GeneratorRunFixture.Ticks, ticks.Length);

        for (int k = 1; k <= ticks.Length; k++)
        {
            var tick = ticks[k - 1];

            Assert.Equal(100 * Math.Min(k, 10), tick.Length);
            Assert.Equal(100, tick.Count(m => m.CameraId == 1));

            if (k >= 10)
            {
                Assert.Equal(1_000, tick.Length);
                Assert.All(Enumerable.Range(1, 10), camera => Assert.Equal(100, tick.Count(m => m.CameraId == camera)));
            }
        }

        var intervals = ticks.Skip(1).Select((tick, i) => TimeSpan.FromTicks(tick[0].TimestampTicks - ticks[i][0].TimestampTicks)).ToArray();
        // Real-time cadence on a shared machine jitters (the first tick also connects ten clients, a loaded run adds delays, and an
        // overrun is followed by one immediate tick): a single interval must never be a burst or a long stall, the average must be
        // close to the one-second period.
        Assert.All(intervals, interval => Assert.InRange(interval, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(3_000)));
        Assert.InRange(TimeSpan.FromTicks((long)intervals.Average(interval => interval.Ticks)), TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(1_300));
    }

    [Fact]
    [Trait("AC", "AC-03")]
    [Trait("BR", "BR-SIM-03")]
    public void Shared_run_all_plates_are_unique()
    {
        var firstMeasurements = _run.Messages.Where(m => m.CameraId == 1).Select(m => m.NumberPlate!).ToArray();

        Assert.Equal(1_500, firstMeasurements.Length);
        Assert.Equal(1_500, firstMeasurements.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("BR", "BR-OPS-01")]
    public void Shared_run_logs_json_lines_with_the_start_up_summary_and_the_seed()
    {
        Assert.NotEmpty(_run.Result.StandardOutput);
        var lines = _run.Result.StandardOutput.Select(line => JsonDocument.Parse(line).RootElement).ToArray();

        var started = Assert.Single(lines, line => line.GetProperty("EventId").GetInt32() == 3001);
        Assert.Equal("Information", started.GetProperty("LogLevel").GetString());
        Assert.Equal(GeneratorRunFixture.Seed, started.GetProperty("State").GetProperty("Seed").GetInt64());
        Assert.True(started.GetProperty("State").GetProperty("SeedConfigured").GetBoolean());
        Assert.Contains(lines, line => line.GetProperty("EventId").GetInt32() == 3005);
        Assert.Equal(string.Empty, _run.Result.StandardError);
    }

    [Fact]
    [Trait("AC", "AC-03")]
    [Trait("BR", "BR-SIM-03")]
    public async Task Two_runs_without_a_seed_sharing_the_state_path_use_no_plate_twice()
    {
        string statePath = _run.NewStatePath();
        var plates = new List<HashSet<string>>();

        for (int run = 0; run < 2; run++)
        {
            string topic = GeneratorRunFixture.UniqueTopic();
            await using var subscriber = await RecordingSubscriber.StartAsync(_run.Broker, topic, Ct);
            var result = await GeneratorProcess.RunAsync(GeneratorProcess.Settings(_run.Broker, topic, statePath, "--Generator:MaxTicks=3"), Ct);

            Assert.Equal(0, result.ExitCode);
            await subscriber.WaitForCountAsync(600, Ct);
            plates.Add(subscriber.Messages.Select(m => m.NumberPlate!).ToHashSet(StringComparer.Ordinal));
        }

        Assert.Equal(300, plates[0].Count);
        Assert.Equal(300, plates[1].Count);
        Assert.Empty(plates[0].Intersect(plates[1]));
        Assert.True(File.Exists(statePath));
    }

    [Fact]
    public async Task Two_runs_with_the_same_seed_produce_identical_plates_and_speeds()
    {
        var runs = new List<(string NumberPlate, int CameraId, float CurrentSpeed)[]>();

        for (int run = 0; run < 2; run++)
        {
            string topic = GeneratorRunFixture.UniqueTopic();
            await using var subscriber = await RecordingSubscriber.StartAsync(_run.Broker, topic, Ct);
            var result = await GeneratorProcess.RunAsync(GeneratorProcess.Settings(_run.Broker, topic, _run.NewStatePath(), "--Generator:Seed=7", "--Generator:MaxTicks=3"), Ct);

            Assert.Equal(0, result.ExitCode);
            await subscriber.WaitForCountAsync(600, Ct);
            runs.Add(subscriber.Messages.OrderBy(m => m.TimestampTicks).ThenBy(m => m.CameraId).ThenBy(m => m.NumberPlate, StringComparer.Ordinal).Select(m => (m.NumberPlate!, m.CameraId, m.CurrentSpeed)).ToArray());
        }

        Assert.Equal(600, runs[0].Length);
        Assert.Equal(runs[0], runs[1]);
    }

    [Fact]
    [Trait("AC", "AC-34")]
    public async Task Run_with_a_disabled_camera_sends_nothing_for_it_and_the_others_are_normal()
    {
        string topic = GeneratorRunFixture.UniqueTopic();
        await using var subscriber = await RecordingSubscriber.StartAsync(_run.Broker, topic, Ct);
        var result = await GeneratorProcess.RunAsync(
            GeneratorProcess.Settings(_run.Broker, topic, _run.NewStatePath(), "--Generator:Seed=7", "--Generator:MaxTicks=10", "--Generator:DisabledCameras:0=7"),
            Ct);

        Assert.Equal(0, result.ExitCode);
        // 100 * (1 + ... + 10) measurements minus camera 7 at ticks 7 to 10.
        await subscriber.WaitForCountAsync(5_500 - 400, Ct);

        Assert.DoesNotContain(subscriber.Messages, m => m.CameraId == 7);
        Assert.Equal(Enumerable.Range(1, 10).Where(camera => camera != 7), subscriber.Messages.Select(m => m.CameraId).Distinct().Order());
        Assert.All(Enumerable.Range(1, 10).Where(camera => camera != 7), camera => Assert.Equal(100 * (11 - camera), subscriber.Messages.Count(m => m.CameraId == camera)));
    }

    [Fact]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-TEL-03")]
    public async Task Run_with_heartbeats_and_a_spawn_limit_sends_heartbeats_exactly_for_idle_cameras()
    {
        string topic = GeneratorRunFixture.UniqueTopic();
        await using var subscriber = await RecordingSubscriber.StartAsync(_run.Broker, topic, Ct);
        var result = await GeneratorProcess.RunAsync(
            GeneratorProcess.Settings(
                _run.Broker, topic, _run.NewStatePath(),
                "--Generator:Seed=7", "--Generator:MaxTicks=8", "--Generator:VehiclesPerTick=5", "--Generator:SpawnUntilTick=3", "--Generator:Heartbeats:Enabled=true"),
            Ct);

        Assert.Equal(0, result.ExitCode);
        // Every tick has 10 messages per camera slot: 5 measurements on a busy camera or 1 heartbeat on an idle one.
        int expected = Enumerable.Range(1, 8).Sum(k => (5 * Math.Min(k, 3)) + (10 - Math.Min(k, 3)));
        await subscriber.WaitForCountAsync(expected, Ct);

        var ticks = subscriber.Messages.GroupBy(m => m.TimestampTicks).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
        Assert.Equal(8, ticks.Length);

        for (int k = 1; k <= ticks.Length; k++)
        {
            var tick = ticks[k - 1];
            var busy = tick.Where(m => m.Type == TelemetryContract.MeasurementType).Select(m => m.CameraId).Distinct().Order().ToArray();
            var heartbeats = tick.Where(m => m.Type == TelemetryContract.HeartbeatType).Select(m => m.CameraId).Order().ToArray();

            Assert.Equal(Enumerable.Range(Math.Max(1, k - 2), Math.Min(k, 3)), busy);
            Assert.Equal(Enumerable.Range(1, 10).Except(busy), heartbeats);
            Assert.Empty(busy.Intersect(heartbeats));
        }
    }

    [Fact]
    public async Task Invalid_settings_end_with_exit_code_one_and_the_error_text()
    {
        var result = await GeneratorProcess.RunAsync(["--Generator:VehiclesPerTick=0", "--Generator:Mqtt:Port=0"], Ct);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("VehiclesPerTick must be at least 1", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Mqtt:Port must be within 1 to 65535", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(result.StandardOutput);
    }
}
