using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>
/// A Mosquitto container with the solution's configuration on a fixed host port, so that the same address stays valid
/// across a container restart (a restart would otherwise get a new random port). Optionally joins a Docker network under
/// the alias <see cref="NetworkAlias"/>, where the processor container finds it.
/// </summary>
public sealed class MosquittoBroker : IAsyncLifetime
{
    public const string NetworkAlias = "broker";

    private readonly IContainer _container;

    public MosquittoBroker()
        : this(null)
    {
    }

    internal MosquittoBroker(INetwork? network)
    {
        Port = FreeTcpPort();
        var configuration = new FileInfo(Path.Combine(AppContext.BaseDirectory, "deploy", "mosquitto.conf"));
        var builder = new ContainerBuilder("eclipse-mosquitto:2")
            .WithResourceMapping(configuration, "/mosquitto/config/")
            .WithPortBinding(Port, 1883)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883));

        if (network is not null)
        {
            builder = builder.WithNetwork(network).WithNetworkAliases(NetworkAlias);
        }

        _container = builder.Build();
    }

    public string Host => "127.0.0.1";

    public int Port { get; }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>Stops and starts the same container; persistent sessions survive because the broker persists them on shutdown.</summary>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await _container.StopAsync(cancellationToken);
        await _container.StartAsync(cancellationToken);
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
