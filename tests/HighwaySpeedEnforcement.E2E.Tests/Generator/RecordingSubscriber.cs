using System.Text.Json;
using HighwaySpeedEnforcement.E2E.Tests.Support;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace HighwaySpeedEnforcement.E2E.Tests.Generator;

/// <summary>One received telemetry message: a measurement (with a plate and speed) or a heartbeat (plate null).</summary>
public sealed record ReceivedMessage(string Type, int CameraId, long TimestampTicks, string? NumberPlate, float CurrentSpeed, bool Duplicate);

/// <summary>
/// Subscribes to the telemetry topic with a persistent session and records every message in arrival order, so that tests can
/// check counts, per-camera order and heartbeats of what the generator published. Reconnects after a broker restart; the
/// persistent session keeps the subscription and the broker queues for it meanwhile.
/// </summary>
public sealed class RecordingSubscriber : IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private readonly MqttClientSubscribeOptions _subscribeOptions;
    private readonly Lock _lock = new();
    private readonly List<ReceivedMessage> _messages = [];
    private readonly CancellationTokenSource _disposed = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RecordingSubscriber(MosquittoBroker broker, string topic)
    {
        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker.Host, broker.Port)
            .WithClientId($"e2e-subscriber-{Guid.NewGuid():N}")
            .WithCleanSession(false)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .Build();
        _subscribeOptions = factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(filter => filter.WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public IReadOnlyList<ReceivedMessage> Messages
    {
        get
        {
            lock (_lock)
            {
                return _messages.ToArray();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _messages.Count;
            }
        }
    }

    public static async Task<RecordingSubscriber> StartAsync(MosquittoBroker broker, string topic, CancellationToken cancellationToken)
    {
        var subscriber = new RecordingSubscriber(broker, topic);
        await subscriber._client.ConnectAsync(subscriber._options, cancellationToken);
        await subscriber._client.SubscribeAsync(subscriber._subscribeOptions, cancellationToken);
        return subscriber;
    }

    /// <summary>Completes once at least <paramref name="count"/> messages were received.</summary>
    public async Task WaitForCountAsync(int count, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? Eventually.DefaultTimeout);

        while (true)
        {
            Task changed;

            lock (_lock)
            {
                if (_messages.Count >= count)
                {
                    return;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for {count} messages; received {Count}.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposed.CancelAsync();
        _client.DisconnectedAsync -= OnDisconnectedAsync;

        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _client.Dispose();
        _disposed.Dispose();
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var payload = System.Buffers.BuffersExtensions.ToArray(e.ApplicationMessage.Payload);
        ReceivedMessage received;

        using (var document = JsonDocument.Parse(payload))
        {
            string type = document.RootElement.GetProperty(TelemetryContract.TypePropertyName).GetString()!;

            if (type == TelemetryContract.MeasurementType)
            {
                var message = JsonSerializer.Deserialize(payload, TelemetryJsonContext.Default.MeasurementMessage)!;
                received = new ReceivedMessage(type, message.CameraId, message.TimestampTicks, message.NumberPlate, message.CurrentSpeed, e.ApplicationMessage.Dup);
            }
            else
            {
                var message = JsonSerializer.Deserialize(payload, TelemetryJsonContext.Default.HeartbeatMessage)!;
                received = new ReceivedMessage(type, message.CameraId, message.TimestampTicks, null, 0f, e.ApplicationMessage.Dup);
            }
        }

        lock (_lock)
        {
            _messages.Add(received);
            _changed.TrySetResult();
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return Task.CompletedTask;
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        // The broker is being restarted by the test; keep trying until it is back or the subscriber is disposed.
        while (!_disposed.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), _disposed.Token);
                await _client.ConnectAsync(_options, _disposed.Token);
                await _client.SubscribeAsync(_subscribeOptions, _disposed.Token);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Broker still down; the loop retries.
            }
        }
    }
}
