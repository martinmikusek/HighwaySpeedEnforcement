using System.Net;

namespace HighwaySpeedEnforcement.Component.Tests;

/// <summary>The operator's status page (<c>GET /ui</c>): served as one self-contained HTML document (BR-OPS-02).</summary>
public sealed class StatusPageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task Ui_serves_a_self_contained_html_page_that_reads_the_status_endpoint()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ui", Ct);
        string html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("/api/status", html, StringComparison.Ordinal);
        Assert.Contains("Latency per message", html, StringComparison.Ordinal);
        Assert.Contains("Age of the newest event", html, StringComparison.Ordinal);
        Assert.Contains("Ingest buffer", html, StringComparison.Ordinal);
        Assert.Contains("Throughput", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-OPS-02")]
    public async Task Ui_page_loads_nothing_from_outside()
    {
        using var factory = new ProcessorFactory();
        using var client = factory.CreateClient();

        string html = await client.GetStringAsync("/ui", Ct);

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link ", html, StringComparison.OrdinalIgnoreCase);
    }
}
