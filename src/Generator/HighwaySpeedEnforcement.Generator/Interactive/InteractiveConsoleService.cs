using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace HighwaySpeedEnforcement.Generator.Interactive;

/// <summary>
/// Runs the <see cref="InteractiveConsole"/> for the lifetime of the host: starts it before the runner (registration order),
/// restores the terminal after the runner stopped. <c>Q</c> stops the application like Ctrl+C.
/// </summary>
public sealed class InteractiveConsoleService : IHostedService
{
    private readonly InteractiveConsole _console;
    private readonly IMeasurementPublisher _publisher;
    private readonly IHostApplicationLifetime _lifetime;

    public InteractiveConsoleService(InteractiveConsole console, IMeasurementPublisher publisher, IHostApplicationLifetime lifetime)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _console.Start(BrokerState, _lifetime.StopApplication);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _console.Dispose();
        return Task.CompletedTask;
    }

    private BrokerState BrokerState()
    {
        if (_publisher is not MqttMeasurementPublisher mqtt || mqtt.ConnectionCount == 0)
        {
            return Interactive.BrokerState.Connecting;
        }

        return mqtt.ConnectedCount == mqtt.ConnectionCount ? Interactive.BrokerState.Connected : Interactive.BrokerState.Reconnecting;
    }
}
