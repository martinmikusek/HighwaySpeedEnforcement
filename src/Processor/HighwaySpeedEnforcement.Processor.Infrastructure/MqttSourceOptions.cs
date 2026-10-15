using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.Processor.Infrastructure;

/// <summary>Connection settings of <see cref="MqttMeasurementSource"/> (design section 6, <c>Mqtt</c> section).</summary>
public sealed class MqttSourceOptions
{
    /// <summary>When false the source is not started; component tests run the processor without a broker.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Use <c>127.0.0.1</c> rather than <c>localhost</c> on the host (see <c>spikes/mqtt/FINDINGS.md</c>); inside compose the service name.</summary>
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public string Topic { get; set; } = TelemetryContract.Topic;

    /// <summary>Fixed id of the persistent session: the broker queues for it while the processor is down (ADR-0001).</summary>
    public string ClientId { get; set; } = TelemetryContract.ProcessorClientId;

    public TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>First delay of the reconnect loop; doubled on every failure up to <see cref="ReconnectDelayMax"/>.</summary>
    public TimeSpan ReconnectDelayMin { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReconnectDelayMax { get; set; } = TimeSpan.FromSeconds(10);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add($"{nameof(Host)} must not be empty.");
        }

        if (Port is < 1 or > 65535)
        {
            errors.Add($"{nameof(Port)} must be between 1 and 65535 (was {Port}).");
        }

        if (string.IsNullOrWhiteSpace(Topic))
        {
            errors.Add($"{nameof(Topic)} must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            errors.Add($"{nameof(ClientId)} must not be empty.");
        }

        if (KeepAlive <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(KeepAlive)} must be greater than zero (was {KeepAlive}).");
        }

        if (ReconnectDelayMin <= TimeSpan.Zero || ReconnectDelayMax < ReconnectDelayMin)
        {
            errors.Add($"{nameof(ReconnectDelayMin)} must be greater than zero and not above {nameof(ReconnectDelayMax)} (were {ReconnectDelayMin}, {ReconnectDelayMax}).");
        }

        return errors;
    }
}
