using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>Reads a container's memory usage through the same Docker endpoint Testcontainers uses (the load test's leak check).</summary>
public sealed class ContainerMemory : IDisposable
{
    private readonly IDockerClient _client = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid()).Build();

    public async Task<long> UsageBytesAsync(string containerId, CancellationToken cancellationToken)
    {
        // Progress<T> posts to a synchronization context asynchronously; the value must be captured before the call returns.
        var capture = new SynchronousProgress();
        await _client.Containers.GetContainerStatsAsync(containerId, new ContainerStatsParameters { Stream = false, OneShot = true }, capture, cancellationToken);
        return capture.UsageBytes;
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private sealed class SynchronousProgress : IProgress<ContainerStatsResponse>
    {
        public long UsageBytes { get; private set; }

        public void Report(ContainerStatsResponse value)
        {
            UsageBytes = (long)(value.MemoryStats?.Usage ?? 0);
        }
    }
}
