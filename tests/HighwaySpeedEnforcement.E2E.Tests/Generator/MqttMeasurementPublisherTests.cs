using System.Diagnostics;
using HighwaySpeedEnforcement.E2E.Tests.Support;
using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.E2E.Tests.Generator;

/// <summary>Step 10 of the generator plan: the MQTT publisher against a real Mosquitto (ADR-0001, BR-SIM-04, AC-33, AC-34).</summary>
[Trait("Category", "E2E")]
public sealed class MqttMeasurementPublisherTests : IClassFixture<MosquittoBroker>
{
    private const ulong Seed = 20261014;
    private static readonly long BaseTicks = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc).Ticks;

    private readonly MosquittoBroker _broker;

    public MqttMeasurementPublisherTests(MosquittoBroker broker)
    {
        _broker = broker;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PublisherMode.PerCamera)]
    [InlineData(PublisherMode.SingleConnection)]
    [Trait("BR", "BR-SIM-04")]
    public async Task Steady_state_tick_delivers_every_message_per_camera_in_order(PublisherMode mode)
    {
        string topic = UniqueTopic();
        await using var subscriber = await RecordingSubscriber.StartAsync(_broker, topic, Ct);
        await using var publisher = CreatePublisher(mode, topic);
        var simulation = CreateSimulation(new SimulationOptions());
        TickOutput tick = null!;

        for (int k = 1; k <= 10; k++)
        {
            tick = simulation.Tick(BaseTicks + (k * TimeSpan.TicksPerSecond));
        }

        Assert.Equal(1_000, tick.Measurements.Count);

        // Warm up the connections once, then measure a full steady-state tick.
        await publisher.PublishTickAsync(simulation.Tick(BaseTicks + (11 * TimeSpan.TicksPerSecond)), Ct);
        await subscriber.WaitForCountAsync(1_000, Ct);
        var stopwatch = Stopwatch.StartNew();
        await publisher.PublishTickAsync(tick, Ct);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{mode}: 1,000 messages acknowledged in {stopwatch.Elapsed.TotalMilliseconds:F0} ms");

        await subscriber.WaitForCountAsync(2_000, Ct);
        var received = subscriber.Messages.Skip(1_000).ToArray();

        Assert.Equal(1_000, received.Length);
        Assert.All(received, message => Assert.Equal(TelemetryContract.MeasurementType, message.Type));

        for (int camera = 1; camera <= 10; camera++)
        {
            var expected = tick.Measurements.Where(m => m.CameraId == camera).Select(m => (m.NumberPlate, m.CurrentSpeed, m.TimestampTicks)).ToArray();
            var actual = received.Where(m => m.CameraId == camera).Select(m => (m.NumberPlate!, m.CurrentSpeed, m.TimestampTicks)).ToArray();

            Assert.Equal(100, actual.Length);
            Assert.Equal(expected, actual);
        }

        Assert.True(stopwatch.Elapsed < SimulationRunnerBudget, $"{mode}: a tick of 1,000 messages took {stopwatch.Elapsed}, more than the one-second interval.");
    }

    [Theory]
    [InlineData(PublisherMode.PerCamera)]
    [InlineData(PublisherMode.SingleConnection)]
    [Trait("AC", "AC-01")]
    [Trait("BR", "BR-SIM-04")]
    public async Task Vehicle_messages_arrive_in_camera_order_across_ticks(PublisherMode mode)
    {
        string topic = UniqueTopic();
        await using var subscriber = await RecordingSubscriber.StartAsync(_broker, topic, Ct);
        await using var publisher = CreatePublisher(mode, topic);
        var simulation = CreateSimulation(new SimulationOptions { VehiclesPerTick = 10 });
        int total = 0;

        for (int k = 1; k <= 12; k++)
        {
            var tick = simulation.Tick(BaseTicks + (k * TimeSpan.TicksPerSecond));
            total += tick.Measurements.Count;
            await publisher.PublishTickAsync(tick, Ct);
        }

        await subscriber.WaitForCountAsync(total, Ct);
        var camerasByPlate = subscriber.Messages.GroupBy(m => m.NumberPlate!).ToDictionary(g => g.Key, g => g.Select(m => m.CameraId).ToArray());

        Assert.Equal(120, camerasByPlate.Count);
        Assert.Equal(30, camerasByPlate.Values.Count(cameras => cameras.SequenceEqual(Enumerable.Range(1, 10))));
        Assert.All(camerasByPlate.Values, cameras => Assert.Equal(Enumerable.Range(1, cameras.Length), cameras));
    }

    [Theory]
    [InlineData(PublisherMode.PerCamera)]
    [InlineData(PublisherMode.SingleConnection)]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-TEL-03")]
    public async Task Heartbeats_arrive_on_the_same_topic_for_idle_cameras(PublisherMode mode)
    {
        string topic = UniqueTopic();
        await using var subscriber = await RecordingSubscriber.StartAsync(_broker, topic, Ct);
        await using var publisher = CreatePublisher(mode, topic);
        var simulation = CreateSimulation(new SimulationOptions { VehiclesPerTick = 3, SpawnUntilTick = 1, HeartbeatsEnabled = true });
        var expectedHeartbeats = new List<(int Camera, long Ticks)>();
        int total = 0;

        for (int k = 1; k <= 3; k++)
        {
            var tick = simulation.Tick(BaseTicks + (k * TimeSpan.TicksPerSecond));
            expectedHeartbeats.AddRange(tick.HeartbeatCameras.Select(camera => (camera, tick.TimestampTicks)));
            total += tick.Measurements.Count + tick.HeartbeatCameras.Count;
            await publisher.PublishTickAsync(tick, Ct);
        }

        await subscriber.WaitForCountAsync(total, Ct);
        var heartbeats = subscriber.Messages.Where(m => m.Type == TelemetryContract.HeartbeatType).Select(m => (m.CameraId, m.TimestampTicks)).ToArray();

        Assert.Equal(27, expectedHeartbeats.Count);
        Assert.Equal(expectedHeartbeats.OrderBy(h => h.Ticks).ThenBy(h => h.Camera), heartbeats.OrderBy(h => h.TimestampTicks).ThenBy(h => h.CameraId));
        Assert.Equal(9, subscriber.Messages.Count(m => m.Type == TelemetryContract.MeasurementType));
    }

    [Theory]
    [InlineData(PublisherMode.PerCamera)]
    [InlineData(PublisherMode.SingleConnection)]
    [Trait("AC", "AC-34")]
    public async Task Disabled_camera_produces_no_messages_and_no_client(PublisherMode mode)
    {
        string topic = UniqueTopic();
        await using var subscriber = await RecordingSubscriber.StartAsync(_broker, topic, Ct);
        await using var publisher = CreatePublisher(mode, topic);
        var simulation = CreateSimulation(new SimulationOptions { VehiclesPerTick = 5, DisabledCameras = [7] });
        int total = 0;

        for (int k = 1; k <= 10; k++)
        {
            var tick = simulation.Tick(BaseTicks + (k * TimeSpan.TicksPerSecond));
            total += tick.Measurements.Count;
            await publisher.PublishTickAsync(tick, Ct);
        }

        await subscriber.WaitForCountAsync(total, Ct);

        // 5 vehicles per tick over 10 ticks: 5 * (1 + ... + 10) measurements, minus camera 7 at ticks 7 to 10.
        Assert.Equal(275 - 20, total);
        Assert.DoesNotContain(subscriber.Messages, m => m.CameraId == 7);
        Assert.Equal(Enumerable.Range(1, 10).Where(camera => camera != 7), subscriber.Messages.Select(m => m.CameraId).Distinct().Order());

        if (mode == PublisherMode.PerCamera)
        {
            Assert.Equal(9, publisher.ClientIds.Count);
            Assert.DoesNotContain(MqttMeasurementPublisher.ClientIdFor(mode, 7, "e2e"), publisher.ClientIds);
        }
        else
        {
            Assert.Equal([MqttMeasurementPublisher.ClientIdFor(mode, 0, "e2e")], publisher.ClientIds);
        }
    }

    [Theory]
    [InlineData(PublisherMode.PerCamera)]
    [InlineData(PublisherMode.SingleConnection)]
    [Trait("BR", "BR-SIM-04")]
    public async Task Broker_outage_during_a_tick_pauses_the_publish_and_nothing_is_lost(PublisherMode mode)
    {
        string topic = UniqueTopic();
        await using var subscriber = await RecordingSubscriber.StartAsync(_broker, topic, Ct);
        await using var publisher = CreatePublisher(mode, topic);
        var simulation = CreateSimulation(new SimulationOptions());
        var ticks = Enumerable.Range(1, 30).Select(k => simulation.Tick(BaseTicks + (k * TimeSpan.TicksPerSecond))).ToArray();
        int total = ticks.Sum(tick => tick.Measurements.Count);
        var cancellation = Ct;

        // Thirty ticks published back to back keep the publisher busy for well over ten seconds, so the restart lands inside it.
        var publishing = Task.Run(
            async () =>
            {
                foreach (var tick in ticks)
                {
                    await publisher.PublishTickAsync(tick, cancellation);
                }
            },
            cancellation);

        await subscriber.WaitForCountAsync(2_000, Ct);
        await _broker.RestartAsync(Ct);

        Assert.False(publishing.IsCompleted, "publishing must wait for the broker instead of completing without it");

        await publishing.WaitAsync(TimeSpan.FromSeconds(120), Ct);
        await subscriber.WaitForCountAsync(total, Ct, TimeSpan.FromSeconds(120));

        var expected = ticks.SelectMany(tick => tick.Measurements).Select(m => (m.NumberPlate, m.CameraId)).ToHashSet();
        var received = subscriber.Messages.Select(m => (m.NumberPlate!, m.CameraId)).ToHashSet();

        Assert.Equal(25_500, total);
        Assert.Empty(expected.Except(received));
        Assert.Empty(received.Except(expected));
        TestContext.Current.TestOutputHelper?.WriteLine($"{mode}: {subscriber.Count} deliveries for {total} messages ({subscriber.Count - total} duplicates after the restart)");
    }

    private static readonly TimeSpan SimulationRunnerBudget = TimeSpan.FromSeconds(1);

    private MqttMeasurementPublisher CreatePublisher(PublisherMode mode, string topic)
    {
        var options = new MqttPublisherOptions
        {
            Host = _broker.Host,
            Port = _broker.Port,
            Topic = topic,
            PublisherMode = mode,
            RunId = "e2e",
        };

        return new MqttMeasurementPublisher(options, new TestOutputLogger<MqttMeasurementPublisher>(), TimeProvider.System);
    }

    private static Simulation CreateSimulation(SimulationOptions options)
    {
        var random = new DeterministicRandom(Seed);
        return new Simulation(options, PlatePermutation.FromSeed(PlateFormat.Size, random), random);
    }

    private static string UniqueTopic()
    {
        return $"{TelemetryContract.Topic}/test/{Guid.NewGuid():N}";
    }
}
