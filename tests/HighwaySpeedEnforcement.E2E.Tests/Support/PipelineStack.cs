using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>
/// The shared part of the whole-system tests: one Docker network, one broker on it and the processor image built once
/// from the real Dockerfile. Each test starts its own processor container (<see cref="StartProcessorAsync"/>) so that it
/// begins with empty boards and its own settings.
/// </summary>
public sealed class PipelineStack : IAsyncLifetime
{
    private readonly INetwork _network;
    private readonly IFutureDockerImage _image;

    public PipelineStack()
    {
        _network = new NetworkBuilder().Build();
        Broker = new MosquittoBroker(_network);
        var repositoryRoot = FindRepositoryRoot();
        _image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(repositoryRoot)
            .WithDockerfile("src/Processor/HighwaySpeedEnforcement.Processor.Api/Dockerfile")
            .WithName("hse-processor:e2e")
            .WithCleanUp(false)
            .Build();
    }

    public MosquittoBroker Broker { get; }

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _network.CreateAsync(cancellationToken);
        await Task.WhenAll(Broker.InitializeAsync().AsTask(), _image.CreateAsync(cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        await Broker.DisposeAsync();
        await _network.DeleteAsync();
        await _image.DisposeAsync();
    }

    /// <summary>Starts a processor container connected to the broker; <paramref name="settings"/> are configuration keys such as <c>Processor:CameraBoardWindow</c>.</summary>
    public async Task<ProcessorContainer> StartProcessorAsync(ProcessorRun settings, CancellationToken cancellationToken)
    {
        var builder = new ContainerBuilder(_image)
            .WithNetwork(_network)
            .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
            .WithEnvironment("Mqtt__Host", MosquittoBroker.NetworkAlias)
            .WithEnvironment("Mqtt__ClientId", $"speed-processor-{Guid.NewGuid():N}")
            .WithEnvironment("Processor__CameraBoardWindow", DurationText(settings.CameraBoardWindow))
            .WithEnvironment("Processor__GlobalBoardLinger", DurationText(settings.GlobalBoardLinger))
            .WithEnvironment("Processor__InactivityTimeout", DurationText(settings.InactivityTimeout))
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8080).ForPath("/health/ready")));

        var container = builder.Build();
        await container.StartAsync(cancellationToken);
        var processor = new ProcessorContainer(container, settings);
        await processor.WaitUntilBrokerConnectedAsync(cancellationToken);
        return processor;
    }

    private static string DurationText(TimeSpan value)
    {
        return value == Timeout.InfiniteTimeSpan ? "Infinite" : value.ToString("c");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Yunex.HighwaySpeedEnforcement.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root (Yunex.HighwaySpeedEnforcement.slnx) was not found above the test output directory.");
    }
}

/// <summary>The processor settings a scenario runs with; the same values drive the <see cref="BoardOracle"/>.</summary>
public sealed record ProcessorRun(TimeSpan CameraBoardWindow, TimeSpan GlobalBoardLinger, TimeSpan InactivityTimeout)
{
    /// <summary>The defaults of the design (window 2 s, linger 3 s, timeout 5 s).</summary>
    public static ProcessorRun Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
}
