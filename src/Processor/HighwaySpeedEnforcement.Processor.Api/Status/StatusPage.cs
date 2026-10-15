using System.Reflection;

namespace HighwaySpeedEnforcement.Processor.Api.Status;

/// <summary>
/// The operator's status page (<c>GET /ui</c>): one self-contained HTML file, embedded in the assembly, that reads
/// <c>/api/status</c> once per second and draws throughput, ingest lag, buffer depth and tracked vehicles (BR-OPS-02).
/// It has no scripts or styles from outside and adds no endpoint besides the page itself.
/// </summary>
public static class StatusPage
{
    private const string ResourceSuffix = "StatusPage.html";

    private static readonly Lazy<string> _html = new(Load);

    public static string Html => _html.Value;

    private static string Load()
    {
        var assembly = typeof(StatusPage).Assembly;
        string name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith(ResourceSuffix, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"The embedded resource {name} is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
