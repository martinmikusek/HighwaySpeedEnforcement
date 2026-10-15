using System.Text.Json;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>Publishes telemetry the way the generator does: one connection, QoS 1, the contract's JSON.</summary>
public sealed class TestPublisher : IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly string _topic;

    private TestPublisher(IMqttClient client, string topic)
    {
        _client = client;
        _topic = topic;
    }

    public static async Task<TestPublisher> ConnectAsync(MosquittoBroker broker, string topic, CancellationToken cancellationToken)
    {
        var client = new MqttClientFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker.Host, broker.Port)
            .WithClientId($"e2e-publisher-{Guid.NewGuid():N}")
            .WithCleanSession(true)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .Build();
        await client.ConnectAsync(options, cancellationToken);
        return new TestPublisher(client, topic);
    }

    public Task PublishMeasurementAsync(string plate, int camera, long timestampTicks, float speed, CancellationToken cancellationToken)
    {
        var message = new MeasurementMessage(TelemetryContract.MeasurementType, plate, timestampTicks, speed, camera);
        return PublishAsync(JsonSerializer.SerializeToUtf8Bytes(message, TelemetryJsonContext.Default.MeasurementMessage), cancellationToken);
    }

    public Task PublishHeartbeatAsync(int camera, long timestampTicks, CancellationToken cancellationToken)
    {
        var message = new HeartbeatMessage(TelemetryContract.HeartbeatType, camera, timestampTicks);
        return PublishAsync(JsonSerializer.SerializeToUtf8Bytes(message, TelemetryJsonContext.Default.HeartbeatMessage), cancellationToken);
    }

    public Task PublishAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(_topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        return _client.PublishAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _client.Dispose();
    }
}
