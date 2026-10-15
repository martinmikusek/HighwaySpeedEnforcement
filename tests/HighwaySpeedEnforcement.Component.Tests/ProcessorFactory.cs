using HighwaySpeedEnforcement.Processor.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HighwaySpeedEnforcement.Component.Tests;

/// <summary>
/// The processor in memory without a broker (<c>Mqtt:Enabled=false</c>): measurements go in through <see cref="IIngestSink"/>
/// and come out through REST, as the testing strategy's component level describes.
/// </summary>
public class ProcessorFactory : WebApplicationFactory<Program>
{
    /// <summary>Configuration overrides; set them before the first <c>CreateClient</c>, which builds the host.</summary>
    public Dictionary<string, string?> Settings { get; } = new() { ["Mqtt:Enabled"] = "false" };

    public IIngestSink Sink => Services.GetRequiredService<IIngestSink>();

    public IngestPipeline Pipeline => Services.GetRequiredService<IngestPipeline>();

    /// <summary>Submits the messages in order and waits until the workers applied them all.</summary>
    public async Task IngestAsync(IEnumerable<IncomingMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await Sink.SubmitAsync(message, cancellationToken);
        }

        await Pipeline.WaitUntilIdleAsync().WaitAsync(cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
