using System.Diagnostics.Metrics;
using HighwaySpeedEnforcement.Processor.Api;
using HighwaySpeedEnforcement.Processor.Api.Configuration;
using HighwaySpeedEnforcement.Processor.Api.Status;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;
using HighwaySpeedEnforcement.Processor.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs to the console (design section 7); levels come from the Logging section of appsettings.json.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";
});

var settings = ProcessorConfiguration.Load(builder.Configuration);

if (settings.Errors.Count > 0)
{
    using var startupLogging = LoggerFactory.Create(logging => logging.AddJsonConsole());
    var startupLogger = startupLogging.CreateLogger("Startup");

    foreach (var error in settings.Errors)
    {
        startupLogger.LogCritical("Invalid configuration: {Error}", error);
    }

    throw new InvalidOperationException($"The processor cannot start, {settings.Errors.Count} configuration error(s): {string.Join(" | ", settings.Errors)}");
}

builder.Services.AddSingleton(settings.Processor);
builder.Services.AddSingleton(settings.Mqtt);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new BoardSet(settings.Processor.ToLivenessPolicy()));
builder.Services.AddSingleton<MeasurementValidator>();
builder.Services.AddSingleton(provider => new DiagnosticsIngestMetrics(
    provider.GetRequiredService<IMeterFactory>(),
    () => provider.GetRequiredService<IngestPipeline>().Status,
    provider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IIngestMetrics>(provider => provider.GetRequiredService<DiagnosticsIngestMetrics>());
builder.Services.AddSingleton<IngestPipeline>();
builder.Services.AddSingleton<IIngestSink>(provider => provider.GetRequiredService<IngestPipeline>());
builder.Services.AddSingleton<SnapshotService>();
builder.Services.AddSingleton<MessageDecoder>();

// Shutdown order (design section 5): hosted services stop in reverse order, so the MQTT source stops before the pipeline drains.
builder.Services.AddHostedService<IngestPipelineHostedService>();

if (settings.Mqtt.Enabled)
{
    builder.Services.AddSingleton<MqttMeasurementSource>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<MqttMeasurementSource>());
}

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/api/traffic-snapshot", (IngestPipeline pipeline, SnapshotService snapshots, HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";

    if (!pipeline.IsRunning)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The processor is not ready to serve snapshots.");
    }

    return Results.Json(SnapshotMapper.ToDto(snapshots.Take()), RestJsonContext.Default.TrafficSnapshotDto);
});

app.MapGet("/health/live", () => Results.Ok(new HealthResponse("Live", "Unknown", "Unknown")));

app.MapGet("/health/ready", (IngestPipeline pipeline, MqttSourceOptions mqtt, IServiceProvider provider) =>
{
    var broker = BrokerResponse.From(mqtt, provider.GetService<MqttMeasurementSource>());
    var body = new HealthResponse(pipeline.IsRunning ? "Ready" : "NotReady", pipeline.IsRunning ? "Running" : "Stopped", broker.State);

    return pipeline.IsRunning ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/status", (IngestPipeline pipeline, DiagnosticsIngestMetrics metrics, ProcessorSettings processor, MqttSourceOptions mqtt, IServiceProvider provider) =>
{
    return Results.Ok(new StatusResponse(
        CountersResponse.From(metrics.Counters),
        IngestResponse.From(pipeline.Status),
        BrokerResponse.From(mqtt, provider.GetService<MqttMeasurementSource>()),
        SettingsResponse.From(processor)));
});

app.MapGet("/ui", (HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";

    return Results.Content(StatusPage.Html, "text/html; charset=utf-8");
});

app.Logger.LogInformation(
    "Processor settings: partitions {PartitionCount}, channel capacity {ChannelCapacity}, camera window {CameraBoardWindow}, global linger {GlobalBoardLinger}, inactivity timeout {InactivityTimeout}, MQTT {MqttEnabled} {MqttHost}:{MqttPort} {MqttTopic}",
    settings.Processor.PartitionCount,
    settings.Processor.IngestChannelCapacity,
    ProcessorConfiguration.FormatDuration(settings.Processor.CameraBoardWindow),
    ProcessorConfiguration.FormatDuration(settings.Processor.GlobalBoardLinger),
    ProcessorConfiguration.FormatDuration(settings.Processor.InactivityTimeout),
    settings.Mqtt.Enabled ? "enabled" : "disabled",
    settings.Mqtt.Host,
    settings.Mqtt.Port,
    settings.Mqtt.Topic);

app.Run();

/// <summary>Entry point; public so that component tests can start the host.</summary>
public partial class Program;
