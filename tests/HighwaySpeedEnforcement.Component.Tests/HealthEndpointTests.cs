using System.Net;
using System.Text.Json.Nodes;
using HighwaySpeedEnforcement.Processor.Application;

namespace HighwaySpeedEnforcement.Component.Tests;

/// <summary>Step 17: health and status endpoints (BR-API-03, BR-OPS-02).</summary>
public sealed class HealthEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("AC", "AC-24")]
    [Trait("BR", "BR-API-03")]
    public async Task Liveness_and_readiness_are_healthy_with_an_idle_input()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live", Ct);
        using var ready = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var body = JsonNode.Parse(await ready.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("Ready", (string)body["status"]!);
        Assert.Equal("Running", (string)body["pipeline"]!);
        Assert.Equal("Disabled", (string)body["broker"]!);
    }

    [Fact]
    [Trait("BR", "BR-API-03")]
    public async Task Readiness_is_unavailable_while_the_pipeline_is_not_running_but_liveness_stays()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();
        await factory.Pipeline.StopAsync(Ct);

        using var ready = await client.GetAsync("/health/ready", Ct);
        using var live = await client.GetAsync("/health/live", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("NotReady", (string)JsonNode.Parse(await ready.Content.ReadAsStringAsync(Ct))!["status"]!);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    [Trait("AC", "AC-29")]
    [Trait("BR", "BR-OPS-02")]
    [Trait("BR", "BR-ING-07")]
    public async Task Status_reports_counters_gauges_broker_state_and_effective_settings()
    {
        using var factory = new ProcessorFactory { Settings = { ["Processor:CameraBoardWindow"] = "Infinite", ["Processor:PartitionCount"] = "2" } };
        using var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow.UtcTicks;
        await factory.IngestAsync(
            [
                new IncomingMeasurement("A", now, 130f, 1),
                new IncomingMeasurement("A", now + 1, 131f, 2),
                new IncomingMeasurement("A", now + 1, 131f, 2),
                new IncomingMeasurement("", now, 130f, 1),
                new IncomingMeasurement("B", now, -1f, 1),
                new IncomingMeasurement("C", now, 130f, 11),
                new IncomingMeasurement("D", now + TimeSpan.TicksPerHour, 130f, 1),
            ],
            Ct);
        factory.Sink.ReportUndecodable(RejectReason.Malformed);

        var status = JsonNode.Parse(await client.GetStringAsync("/api/status", Ct))!;

        var counters = status["counters"]!;
        Assert.Equal(8, (long)counters["received"]!);
        Assert.Equal(2, (long)counters["accepted"]!);
        Assert.Equal(0, (long)counters["ignored"]!);
        Assert.Equal(6, (long)counters["rejectedTotal"]!);
        Assert.Equal(1, (long)counters["rejected"]!["OutOfSequence"]!);
        Assert.Equal(1, (long)counters["rejected"]!["InvalidPlate"]!);
        Assert.Equal(1, (long)counters["rejected"]!["InvalidSpeed"]!);
        Assert.Equal(1, (long)counters["rejected"]!["InvalidCamera"]!);
        Assert.Equal(1, (long)counters["rejected"]!["InvalidTimestamp"]!);
        Assert.Equal(1, (long)counters["rejected"]!["Malformed"]!);
        Assert.Equal(0, (long)counters["rejected"]!["UnknownType"]!);

        var ingest = status["ingest"]!;
        Assert.True((bool)ingest["isRunning"]!);
        Assert.Equal(1, (int)ingest["trackedVehicles"]!);
        Assert.Equal(0, (int)ingest["bufferDepth"]!);
        Assert.Equal(2, ingest["partitionDepths"]!.AsArray().Count);
        Assert.Equal(new DateTimeOffset(now + 1, TimeSpan.Zero), (DateTimeOffset)ingest["newestProcessedEventUtc"]!);
        Assert.InRange((double)ingest["ingestLagSeconds"]!, 0, 60);
        Assert.NotNull(ingest["lastReceivedAtUtc"]);

        var broker = status["broker"]!;
        Assert.False((bool)broker["enabled"]!);
        Assert.False((bool)broker["connected"]!);
        Assert.Equal("Disabled", (string)broker["state"]!);

        var settings = status["settings"]!;
        Assert.Equal(2, (int)settings["partitionCount"]!);
        Assert.Equal("Infinite", (string)settings["cameraBoardWindow"]!);
        Assert.Equal("00:00:03", (string)settings["globalBoardLinger"]!);
        Assert.True((bool)settings["heartbeatsEnabled"]!);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task Status_reports_the_latency_of_processed_messages_and_nothing_while_idle()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();

        var idle = JsonNode.Parse(await client.GetStringAsync("/api/status", Ct))!["ingest"]!;
        Assert.Null(idle["latencyAvgSeconds"]);
        Assert.Null(idle["latencyMaxSeconds"]);
        Assert.Equal(0, (long)idle["latencySamples"]!);

        var eventTime = DateTimeOffset.UtcNow.UtcTicks - TimeSpan.FromMilliseconds(250).Ticks;
        await factory.IngestAsync(
            [
                new IncomingMeasurement("A", eventTime, 130f, 1),
                new IncomingMeasurement("B", eventTime, 131f, 1),
                new IncomingMeasurement("C", eventTime, 132f, 1),
            ],
            Ct);

        var ingest = JsonNode.Parse(await client.GetStringAsync("/api/status", Ct))!["ingest"]!;
        Assert.Equal(3, (long)ingest["latencySamples"]!);
        Assert.InRange((double)ingest["latencyAvgSeconds"]!, 0.25, 30);
        Assert.True((double)ingest["latencyMaxSeconds"]! >= (double)ingest["latencyAvgSeconds"]!);
    }
}
