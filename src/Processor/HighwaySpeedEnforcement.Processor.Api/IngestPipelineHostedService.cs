using HighwaySpeedEnforcement.Processor.Application;

namespace HighwaySpeedEnforcement.Processor.Api;

/// <summary>
/// Starts the pipeline workers with the host and stops them after the MQTT source (hosted services stop in reverse
/// registration order), letting the queued items drain for at most <see cref="DrainTimeout"/> (design section 5).
/// </summary>
public sealed class IngestPipelineHostedService : IHostedService
{
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly IngestPipeline _pipeline;

    public IngestPipelineHostedService(IngestPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pipeline.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drain.CancelAfter(DrainTimeout);
        await _pipeline.StopAsync(drain.Token);
    }
}
