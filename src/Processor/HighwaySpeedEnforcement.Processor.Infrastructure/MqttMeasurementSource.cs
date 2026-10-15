using HighwaySpeedEnforcement.Processor.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace HighwaySpeedEnforcement.Processor.Infrastructure;

/// <summary>
/// Input adapter of ADR-0001: subscribes to the telemetry topic with a persistent session (QoS 1, clean session off),
/// decodes every message and hands it to the <see cref="IIngestSink"/> in arrival order; the message is acknowledged
/// only after <see cref="IIngestSink.SubmitAsync"/> returned, so a full pipeline buffer holds the acknowledgement back
/// and the broker keeps the backlog. The message handler is attached before the first connect because the broker
/// delivers queued messages immediately on CONNECT (<c>spikes/mqtt/FINDINGS.md</c>). Reconnects with exponential backoff.
/// </summary>
public sealed partial class MqttMeasurementSource : BackgroundService
{
    private readonly MqttSourceOptions _options;
    private readonly IIngestSink _sink;
    private readonly MessageDecoder _decoder;
    private readonly IIngestMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MqttMeasurementSource> _logger;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _clientOptions;
    private readonly MqttClientSubscribeOptions _subscribeOptions;
    private CancellationToken _stoppingToken;
    private TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _subscribed;
    private long _lastConnectedTicks;

    public MqttMeasurementSource(
        MqttSourceOptions options,
        IIngestSink sink,
        MessageDecoder decoder,
        IIngestMetrics metrics,
        TimeProvider timeProvider,
        ILogger<MqttMeasurementSource> logger)
    {
        _options = options;
        _sink = sink;
        _decoder = decoder;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _logger = logger;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(options.Host, options.Port)
            .WithClientId(options.ClientId)
            .WithCleanSession(false)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithKeepAlivePeriod(options.KeepAlive)
            .Build();
        _subscribeOptions = factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(filter => filter.WithTopic(options.Topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    /// <summary>True while the client is connected and the subscription is in place.</summary>
    public bool IsConnected => _subscribed && _client.IsConnected;

    public DateTimeOffset? LastConnectedAt
    {
        get
        {
            long ticks = Volatile.Read(ref _lastConnectedTicks);
            return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        var delay = _options.ReconnectDelayMin;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var result = await _client.ConnectAsync(_clientOptions, stoppingToken).ConfigureAwait(false);
                    await _client.SubscribeAsync(_subscribeOptions, stoppingToken).ConfigureAwait(false);
                    _subscribed = true;
                    Volatile.Write(ref _lastConnectedTicks, _timeProvider.GetUtcNow().UtcTicks);
                    delay = _options.ReconnectDelayMin;
                    LogConnected(_options.Host, _options.Port, _options.Topic, result.IsSessionPresent);

                    await _disconnected.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    LogConnectFailed(exception, _options.Host, _options.Port, delay);
                }

                _subscribed = false;
                await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.ReconnectDelayMax.Ticks));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown: fall through to the clean disconnect.
        }
        finally
        {
            _subscribed = false;
            await DisconnectAsync().ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }

    private async Task DisconnectAsync()
    {
        if (!_client.IsConnected)
        {
            return;
        }

        try
        {
            // The subscription stays in the persistent session on purpose: the broker queues for the processor while it is down.
            await _client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None).ConfigureAwait(false);
            LogDisconnectedByUs();
        }
        catch (Exception exception)
        {
            LogDisconnectFailed(exception);
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _subscribed = false;

        if (e.ClientWasConnected && !_stoppingToken.IsCancellationRequested)
        {
            LogConnectionLost(e.Exception, e.Reason, e.ReasonString);
        }

        _disconnected.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        e.AutoAcknowledge = false;
        var payload = e.ApplicationMessage.Payload;
        var result = payload.IsSingleSegment ? _decoder.Decode(payload.FirstSpan) : _decoder.Decode(System.Buffers.BuffersExtensions.ToArray(in payload));

        try
        {
            switch (result.Outcome)
            {
                case DecodeOutcome.Decoded:
                    await _sink.SubmitAsync(result.Message!, _stoppingToken).ConfigureAwait(false);
                    break;

                case DecodeOutcome.Ignored:
                    _metrics.Received();
                    _metrics.Ignored();
                    break;

                default:
                    _sink.ReportUndecodable(result.RejectReason);
                    break;
            }

            await e.AcknowledgeAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Not acknowledged: the broker redelivers it to the next session (ADR-0001).
        }
        catch (Exception exception)
        {
            // Not acknowledged either; the sink failed, so the message is retried after a reconnect rather than lost.
            LogSubmitFailed(exception);
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "MQTT connected to {Host}:{Port}, subscribed to {Topic} (session present: {SessionPresent})")]
    private partial void LogConnected(string host, int port, string topic, bool sessionPresent);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "MQTT connection to {Host}:{Port} failed; retrying in {Delay}")]
    private partial void LogConnectFailed(Exception exception, string host, int port, TimeSpan delay);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "MQTT connection lost: {Reason} {ReasonString}")]
    private partial void LogConnectionLost(Exception? exception, MqttClientDisconnectReason reason, string? reasonString);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "MQTT disconnected on shutdown")]
    private partial void LogDisconnectedByUs();

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "MQTT disconnect on shutdown failed")]
    private partial void LogDisconnectFailed(Exception exception);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Submitting a message to the pipeline failed; it stays unacknowledged")]
    private partial void LogSubmitFailed(Exception exception);
}
