using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Containers;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>A running processor container and its REST API, as the client sees it.</summary>
public sealed class ProcessorContainer : IAsyncDisposable
{
    private readonly IContainer _container;
    private readonly HttpClient _http;

    public ProcessorContainer(IContainer container, ProcessorRun settings)
    {
        _container = container;
        Settings = settings;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{container.GetMappedPublicPort(8080)}/") };
    }

    public ProcessorRun Settings { get; }

    public string Id => _container.Id;

    /// <summary>The REST base address on the host, for a client under test that brings its own <see cref="HttpClient"/>.</summary>
    public Uri BaseAddress => _http.BaseAddress!;

    public async Task<TrafficSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return (await _http.GetFromJsonAsync("api/traffic-snapshot", RestJsonContext.Default.TrafficSnapshotDto, cancellationToken))!;
    }

    public async Task<JsonNode> GetStatusAsync(CancellationToken cancellationToken)
    {
        return JsonNode.Parse(await _http.GetStringAsync("api/status", cancellationToken))!;
    }

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken)
    {
        return _http.GetAsync(path, cancellationToken);
    }

    /// <summary>Completes once the processor has applied <paramref name="sent"/> messages (received, and each accepted or rejected).</summary>
    public Task WaitUntilAppliedAsync(long sent, CancellationToken cancellationToken)
    {
        return Eventually.TrueAsync(
            async () =>
            {
                var counters = (await GetStatusAsync(cancellationToken))["counters"]!;
                return (long)counters["received"]! == sent && (long)counters["accepted"]! + (long)counters["rejectedTotal"]! == sent;
            },
            $"the processor applied {sent} messages",
            cancellationToken);
    }

    public Task WaitUntilBrokerConnectedAsync(CancellationToken cancellationToken)
    {
        return Eventually.TrueAsync(
            async () => (bool)(await GetStatusAsync(cancellationToken))["broker"]!["connected"]!,
            "the processor connected to the broker",
            cancellationToken);
    }

    public async Task<string> LogsAsync()
    {
        var (stdout, stderr) = await _container.GetLogsAsync();
        return stdout + stderr;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _container.DisposeAsync();
    }
}
