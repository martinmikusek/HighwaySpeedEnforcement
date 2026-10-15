using System.Collections.Concurrent;
using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace HighwaySpeedEnforcement.Generator.Infrastructure;

/// <summary>
/// Output adapter of design section 5 (ADR-0001): MQTT 3.1.1, QoS 1, no retain, one topic. <see cref="PublisherMode.PerCamera"/>
/// gives every camera its own client (created on first use, so a disabled camera never connects) and the clients publish in
/// parallel, each its camera's messages one after another; <see cref="PublisherMode.SingleConnection"/> pipelines the tick
/// through one client. A tick is complete when every message is acknowledged; a lost connection is retried with exponential
/// backoff and the client resends from the failed message in order (a duplicate is possible, the processor rejects it by
/// the sequence rule, BR-ING-02).
/// </summary>
public sealed partial class MqttMeasurementPublisher : IMeasurementPublisher
{
    public static readonly TimeSpan ReconnectDelayMin = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ReconnectDelayMax = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

    private const int SingleConnectionKey = 0;

    private readonly MqttPublisherOptions _options;
    private readonly ILogger<MqttMeasurementPublisher> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly MqttClientFactory _factory = new();
    private readonly ConcurrentDictionary<int, Connection> _connections = new();

    public MqttMeasurementPublisher(MqttPublisherOptions options, ILogger<MqttMeasurementPublisher> logger, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Client ids of the connections created so far (a disabled camera never appears).</summary>
    public IReadOnlyCollection<string> ClientIds => _connections.Values.Select(connection => connection.ClientId).ToArray();

    /// <summary>Connections created so far; zero before the first tick is published.</summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>Connections that are connected right now; fewer than <see cref="ConnectionCount"/> while reconnecting.</summary>
    public int ConnectedCount => _connections.Values.Count(connection => connection.IsConnected);

    /// <summary>The client id of design section 5: <c>speed-camera-&lt;cameraId&gt;-&lt;runId&gt;</c> or <c>speed-generator-&lt;runId&gt;</c>.</summary>
    public static string ClientIdFor(PublisherMode mode, int cameraId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return mode switch
        {
            PublisherMode.PerCamera => $"speed-camera-{cameraId}-{runId}",
            PublisherMode.SingleConnection => $"{TelemetryContract.GeneratorClientIdPrefix}{runId}",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown publisher mode."),
        };
    }

    public Task PublishTickAsync(TickOutput tick, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tick);

        return _options.PublisherMode == PublisherMode.PerCamera
            ? PublishPerCameraAsync(tick, cancellationToken)
            : PublishSingleConnectionAsync(tick, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _connections.Clear();
    }

    private async Task PublishPerCameraAsync(TickOutput tick, CancellationToken cancellationToken)
    {
        var payloadsByCamera = new Dictionary<int, List<byte[]>>();

        // Measurements are already ordered by camera, then by vehicle age; the per-camera lists keep that order.
        foreach (var measurement in tick.Measurements)
        {
            if (!payloadsByCamera.TryGetValue(measurement.CameraId, out var payloads))
            {
                payloads = new List<byte[]>(tick.Measurements.Count / Simulation.CameraCount + 1);
                payloadsByCamera[measurement.CameraId] = payloads;
            }

            payloads.Add(MessageEncoder.EncodeMeasurement(measurement));
        }

        foreach (int camera in tick.HeartbeatCameras)
        {
            if (!payloadsByCamera.TryGetValue(camera, out var payloads))
            {
                payloads = [];
                payloadsByCamera[camera] = payloads;
            }

            payloads.Add(MessageEncoder.EncodeHeartbeat(camera, tick.TimestampTicks));
        }

        var tasks = payloadsByCamera.Select(pair => GetConnection(pair.Key).PublishSequentiallyAsync(pair.Value, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private Task PublishSingleConnectionAsync(TickOutput tick, CancellationToken cancellationToken)
    {
        var payloads = new List<byte[]>(tick.Measurements.Count + tick.HeartbeatCameras.Count);

        foreach (var measurement in tick.Measurements)
        {
            payloads.Add(MessageEncoder.EncodeMeasurement(measurement));
        }

        foreach (int camera in tick.HeartbeatCameras)
        {
            payloads.Add(MessageEncoder.EncodeHeartbeat(camera, tick.TimestampTicks));
        }

        return GetConnection(SingleConnectionKey).PublishPipelinedAsync(payloads, cancellationToken);
    }

    private Connection GetConnection(int camera)
    {
        int key = _options.PublisherMode == PublisherMode.PerCamera ? camera : SingleConnectionKey;

        return _connections.GetOrAdd(key, _ => new Connection(this, ClientIdFor(_options.PublisherMode, camera, _options.RunId)));
    }

    private MqttApplicationMessage BuildMessage(byte[] payload)
    {
        return new MqttApplicationMessageBuilder()
            .WithTopic(_options.Topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "MQTT client {ClientId} connected to {Host}:{Port}")]
    private partial void LogConnected(string clientId, string host, int port);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "MQTT client {ClientId} could not connect to {Host}:{Port} ({Reason}); retrying in {Delay}")]
    private partial void LogConnectFailed(string clientId, string host, int port, string reason, TimeSpan delay);

    [LoggerMessage(EventId = 2106, Level = LogLevel.Debug, Message = "MQTT client {ClientId}: details of the failed connect attempt")]
    private partial void LogConnectFailedDetail(Exception exception, string clientId);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Warning, Message = "MQTT client {ClientId} lost its connection: {Reason}")]
    private partial void LogConnectionLost(Exception? exception, string clientId, MqttClientDisconnectReason reason);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Warning, Message = "MQTT client {ClientId}: publishing failed at message {Index} of {Count} ({Reason}); reconnecting and resending from it")]
    private partial void LogPublishFailed(string clientId, int index, int count, string reason);

    [LoggerMessage(EventId = 2107, Level = LogLevel.Debug, Message = "MQTT client {ClientId}: details of the failed publish")]
    private partial void LogPublishFailedDetail(Exception exception, string clientId);

    [LoggerMessage(EventId = 2105, Level = LogLevel.Information, Message = "MQTT client {ClientId} disconnected")]
    private partial void LogDisconnected(string clientId);

    /// <summary>One MQTT client: connects lazily, reconnects with backoff, publishes in order and resends from a failure.</summary>
    private sealed class Connection : IAsyncDisposable
    {
        private readonly MqttMeasurementPublisher _owner;
        private readonly IMqttClient _client;
        private readonly MqttClientOptions _clientOptions;
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        public Connection(MqttMeasurementPublisher owner, string clientId)
        {
            _owner = owner;
            ClientId = clientId;
            _client = owner._factory.CreateMqttClient();
            _clientOptions = new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .WithTcpServer(owner._options.Host, owner._options.Port)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(KeepAlive)
                .Build();
            _client.DisconnectedAsync += OnDisconnectedAsync;
        }

        public string ClientId { get; }

        public bool IsConnected => _client.IsConnected;

        public async Task PublishSequentiallyAsync(IReadOnlyList<byte[]> payloads, CancellationToken cancellationToken)
        {
            int index = 0;

            while (index < payloads.Count)
            {
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    for (; index < payloads.Count; index++)
                    {
                        await _client.PublishAsync(_owner.BuildMessage(payloads[index]), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _owner.LogPublishFailed(ClientId, index, payloads.Count, Describe(exception));
                    _owner.LogPublishFailedDetail(exception, ClientId);
                }
            }
        }

        /// <summary>
        /// Starts every publish without awaiting the previous acknowledgement, then awaits them together. The order on the
        /// wire is the start order: MQTTnet serializes packet writes through a FIFO semaphore (verified by an E2E test).
        /// </summary>
        public async Task PublishPipelinedAsync(IReadOnlyList<byte[]> payloads, CancellationToken cancellationToken)
        {
            int index = 0;

            while (index < payloads.Count)
            {
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

                var inFlight = new List<Task>(payloads.Count - index);

                try
                {
                    // Starting a publish throws synchronously when the connection drops half way through the loop, so the
                    // loop belongs inside the try: the publishes started so far are settled and the rest is resent.
                    for (int i = index; i < payloads.Count; i++)
                    {
                        inFlight.Add(_client.PublishAsync(_owner.BuildMessage(payloads[i]), cancellationToken));
                    }

                    await Task.WhenAll(inFlight).ConfigureAwait(false);
                    index = payloads.Count;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    await SettleAsync(inFlight).ConfigureAwait(false);

                    int failed = inFlight.FindIndex(task => !task.IsCompletedSuccessfully);
                    index += failed < 0 ? inFlight.Count : failed;
                    _owner.LogPublishFailed(ClientId, index, payloads.Count, Describe(exception));
                    _owner.LogPublishFailedDetail(exception, ClientId);
                }
            }
        }

        /// <summary>The first line of the innermost exception's message: the one-line reason of a Warning (the stack goes to Debug).</summary>
        private static string Describe(Exception exception)
        {
            var innermost = exception;

            while (innermost.InnerException is { } inner)
            {
                innermost = inner;
            }

            string message = innermost.Message;
            int newline = message.IndexOfAny(['\r', '\n']);

            return newline < 0 ? message : message[..newline];
        }

        private static async Task SettleAsync(List<Task> tasks)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Only the outcome of each task matters here (the caller inspects it), not the exception.
            }
        }

        public async ValueTask DisposeAsync()
        {
            _client.DisconnectedAsync -= OnDisconnectedAsync;

            if (_client.IsConnected)
            {
                try
                {
                    await _client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None).ConfigureAwait(false);
                    _owner.LogDisconnected(ClientId);
                }
                catch (Exception)
                {
                    // Disconnecting is best effort; the broker drops a clean session anyway.
                }
            }

            _client.Dispose();
            _connectLock.Dispose();
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (_client.IsConnected)
            {
                return;
            }

            await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var delay = ReconnectDelayMin;

                while (!_client.IsConnected)
                {
                    try
                    {
                        await _client.ConnectAsync(_clientOptions, cancellationToken).ConfigureAwait(false);
                        _owner.LogConnected(ClientId, _owner._options.Host, _owner._options.Port);
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        _owner.LogConnectFailed(ClientId, _owner._options.Host, _owner._options.Port, Describe(exception), delay);
                        _owner.LogConnectFailedDetail(exception, ClientId);
                        await Task.Delay(delay, _owner._timeProvider, cancellationToken).ConfigureAwait(false);
                        delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, ReconnectDelayMax.Ticks));
                    }
                }
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            if (e.ClientWasConnected)
            {
                _owner.LogConnectionLost(e.Exception, ClientId, e.Reason);
            }

            return Task.CompletedTask;
        }
    }
}
