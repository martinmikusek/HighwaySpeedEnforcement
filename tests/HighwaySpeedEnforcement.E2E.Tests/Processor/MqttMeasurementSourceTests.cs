using System.Text;
using HighwaySpeedEnforcement.E2E.Tests.Support;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Infrastructure;

namespace HighwaySpeedEnforcement.E2E.Tests.Processor;

/// <summary>Step 13 of the processor plan: the MQTT source against a real Mosquitto (ADR-0001, BR-ING-03).</summary>
[Trait("Category", "E2E")]
public sealed class MqttMeasurementSourceTests : IClassFixture<MosquittoBroker>
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);
    private static readonly long BaseTicks = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc).Ticks;

    private readonly MosquittoBroker _broker;

    public MqttMeasurementSourceTests(MosquittoBroker broker)
    {
        _broker = broker;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "BR-ING-03")]
    public async Task Messages_arrive_in_publication_order()
    {
        var topic = UniqueTopic();
        var sink = new RecordingSink();
        await using var source = await StartedSourceAsync(topic, UniqueClientId(), sink);
        await using var publisher = await TestPublisher.ConnectAsync(_broker, topic, Ct);

        var expected = new List<IncomingMeasurement>();
        for (var camera = 1; camera <= 10; camera++)
        {
            for (var vehicle = 0; vehicle < 50; vehicle++)
            {
                var measurement = new IncomingMeasurement($"P{vehicle:D3}", BaseTicks + camera * TimeSpan.TicksPerSecond, 120f + vehicle, camera);
                expected.Add(measurement);
                await publisher.PublishMeasurementAsync(measurement.NumberPlate!, camera, measurement.TimestampTicks, measurement.CurrentSpeed, Ct);
            }
        }

        await sink.WaitForMessagesAsync(expected.Count, Ct).WaitAsync(StepTimeout, Ct);

        Assert.Equal(expected, sink.Messages);
        Assert.Empty(sink.Undecodable);
    }

    [Fact]
    [Trait("BR", "BR-ING-07")]
    public async Task Undecodable_payloads_are_reported_and_do_not_stop_the_source()
    {
        var topic = UniqueTopic();
        var sink = new RecordingSink();
        await using var source = await StartedSourceAsync(topic, UniqueClientId(), sink, heartbeatsEnabled: false);
        await using var publisher = await TestPublisher.ConnectAsync(_broker, topic, Ct);

        await publisher.PublishAsync(Encoding.UTF8.GetBytes("not json"), Ct);
        await publisher.PublishAsync(Encoding.UTF8.GetBytes("""{"Type":"Telemetry"}"""), Ct);
        await publisher.PublishHeartbeatAsync(3, BaseTicks, Ct);
        await publisher.PublishMeasurementAsync("OK1", 1, BaseTicks, 130f, Ct);

        await sink.WaitForMessagesAsync(1, Ct).WaitAsync(StepTimeout, Ct);

        Assert.Equal([RejectReason.Malformed, RejectReason.UnknownType], sink.Undecodable);
        Assert.Equal(1, sink.IgnoredCount);
        Assert.Equal([new IncomingMeasurement("OK1", BaseTicks, 130f, 1)], sink.Messages);
    }

    [Fact]
    [Trait("BR", "BR-ING-03")]
    public async Task Message_is_acknowledged_only_after_the_submission_completes()
    {
        var topic = UniqueTopic();
        var clientId = UniqueClientId();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingSink = new RecordingSink
        {
            // Holds the submission open until the source is stopped: the message must then stay unacknowledged.
            Gate = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
        };

        await using (var blocked = await StartedSourceAsync(topic, clientId, blockingSink))
        {
            await using var publisher = await TestPublisher.ConnectAsync(_broker, topic, Ct);
            await publisher.PublishMeasurementAsync("HELD", 1, BaseTicks, 130f, Ct);
            await entered.Task.WaitAsync(StepTimeout, Ct);
            await blocked.StopAsync(Ct);
        }

        Assert.Empty(blockingSink.Messages);

        var sink = new RecordingSink();
        await using var resumed = await StartedSourceAsync(topic, clientId, sink);
        await sink.WaitForMessagesAsync(1, Ct).WaitAsync(StepTimeout, Ct);

        Assert.Equal([new IncomingMeasurement("HELD", BaseTicks, 130f, 1)], sink.Messages);
    }

    [Fact]
    [Trait("BR", "BR-ING-03")]
    public async Task Messages_published_while_the_source_is_down_are_delivered_after_it_returns()
    {
        var topic = UniqueTopic();
        var clientId = UniqueClientId();
        await using var publisher = await TestPublisher.ConnectAsync(_broker, topic, Ct);

        var first = new RecordingSink();
        await using (var initial = await StartedSourceAsync(topic, clientId, first))
        {
            await publisher.PublishMeasurementAsync("A", 1, BaseTicks, 130f, Ct);
            await first.WaitForMessagesAsync(1, Ct).WaitAsync(StepTimeout, Ct);
            await initial.StopAsync(Ct);
        }

        for (var camera = 2; camera <= 10; camera++)
        {
            await publisher.PublishMeasurementAsync("A", camera, BaseTicks + camera, 130f, Ct);
        }

        var second = new RecordingSink();
        await using var returned = await StartedSourceAsync(topic, clientId, second);
        await second.WaitForMessagesAsync(9, Ct).WaitAsync(StepTimeout, Ct);

        Assert.Equal(Enumerable.Range(2, 9), second.Messages.Cast<IncomingMeasurement>().Select(m => m.CameraId));
    }

    [Fact]
    [Trait("BR", "BR-OPS-01")]
    public async Task Source_reconnects_after_the_broker_restarts()
    {
        var topic = UniqueTopic();
        var sink = new RecordingSink();
        await using var source = await StartedSourceAsync(topic, UniqueClientId(), sink);

        await using (var before = await TestPublisher.ConnectAsync(_broker, topic, Ct))
        {
            await before.PublishMeasurementAsync("B", 1, BaseTicks, 130f, Ct);
            await sink.WaitForMessagesAsync(1, Ct).WaitAsync(StepTimeout, Ct);
        }

        var connectedBefore = source.Source.LastConnectedAt;
        Assert.NotNull(connectedBefore);

        // The source often reconnects before RestartAsync returns (its backoff is shorter than the container readiness
        // wait), so the disconnected state is transient and must not be polled for. A newer LastConnectedAt is durable
        // evidence that a new connection was established after the restart.
        await _broker.RestartAsync(Ct);
        await Eventually.TrueAsync(() => source.IsConnected && source.Source.LastConnectedAt > connectedBefore, "the source reconnected after the broker restart", Ct);

        await using var after = await TestPublisher.ConnectAsync(_broker, topic, Ct);
        await after.PublishMeasurementAsync("B", 2, BaseTicks + 1, 131f, Ct);
        await sink.WaitForMessagesAsync(2, Ct).WaitAsync(StepTimeout, Ct);

        Assert.Equal([1, 2], sink.Messages.Cast<IncomingMeasurement>().Select(m => m.CameraId));
    }

    [Fact]
    public async Task Shutdown_disconnects_cleanly()
    {
        await using var source = await StartedSourceAsync(UniqueTopic(), UniqueClientId(), new RecordingSink());
        Assert.True(source.IsConnected);

        await source.StopAsync(Ct);

        Assert.False(source.IsConnected);
        Assert.NotNull(source.Source.LastConnectedAt);
        source.Source.Dispose();
    }

    private async Task<RunningSource> StartedSourceAsync(string topic, string clientId, RecordingSink sink, bool heartbeatsEnabled = true)
    {
        var options = new MqttSourceOptions
        {
            Host = _broker.Host,
            Port = _broker.Port,
            Topic = topic,
            ClientId = clientId,
            ReconnectDelayMin = TimeSpan.FromMilliseconds(200),
            ReconnectDelayMax = TimeSpan.FromSeconds(2),
        };
        var source = new MqttMeasurementSource(
            options,
            sink,
            new MessageDecoder(new ProcessorSettings { HeartbeatsEnabled = heartbeatsEnabled }),
            sink,
            TimeProvider.System,
            new TestOutputLogger<MqttMeasurementSource>());

        await source.StartAsync(Ct);
        await Eventually.TrueAsync(() => source.IsConnected, "the source connected and subscribed", Ct);
        return new RunningSource(source);
    }

    private static string UniqueTopic()
    {
        return $"highway/telemetry/v1/e2e/{Guid.NewGuid():N}";
    }

    private static string UniqueClientId()
    {
        return $"e2e-processor-{Guid.NewGuid():N}";
    }
}
