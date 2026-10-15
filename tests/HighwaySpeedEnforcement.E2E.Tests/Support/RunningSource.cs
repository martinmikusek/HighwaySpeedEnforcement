using HighwaySpeedEnforcement.Processor.Infrastructure;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>Owns a started <see cref="MqttMeasurementSource"/> and stops it when the test ends, whatever the outcome.</summary>
public sealed class RunningSource : IAsyncDisposable
{
    public RunningSource(MqttMeasurementSource source)
    {
        Source = source;
    }

    public MqttMeasurementSource Source { get; }

    public bool IsConnected => Source.IsConnected;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Source.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Source.StopAsync(CancellationToken.None);
        Source.Dispose();
    }
}
