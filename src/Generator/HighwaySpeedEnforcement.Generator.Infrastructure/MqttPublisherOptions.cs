using System.Security.Cryptography;

namespace HighwaySpeedEnforcement.Generator.Infrastructure;

/// <summary>
/// Transport options of the MQTT publisher (design section 6, bound from <c>Generator:Mqtt</c>). A plain object: the host binds it
/// and calls <see cref="Validate"/> before anything is built.
/// </summary>
public sealed class MqttPublisherOptions
{
    private const int RunIdBytes = 3;

    private string? _runId;

    /// <summary>Broker host; <c>127.0.0.1</c> rather than <c>localhost</c>, which is slow with Docker Desktop (spike findings).</summary>
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public string Topic { get; set; } = "highway/telemetry/v1";

    public PublisherMode PublisherMode { get; set; } = PublisherMode.PerCamera;

    /// <summary>Distinguishes the client ids of this run from those of an earlier run; a short random id when not configured.</summary>
    public string RunId
    {
        get => _runId ??= GenerateRunId();
        set => _runId = value;
    }

    /// <summary>Every violated rule as one message; empty when the options are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add($"{nameof(Host)} must not be blank.");
        }

        if (Port is < 1 or > 65_535)
        {
            errors.Add($"{nameof(Port)} must be within 1 to 65535 (was {Port}).");
        }

        if (string.IsNullOrWhiteSpace(Topic))
        {
            errors.Add($"{nameof(Topic)} must not be blank.");
        }

        if (!Enum.IsDefined(PublisherMode))
        {
            errors.Add($"{nameof(PublisherMode)} must be {PublisherMode.PerCamera} or {PublisherMode.SingleConnection} (was {PublisherMode}).");
        }

        if (string.IsNullOrWhiteSpace(RunId))
        {
            errors.Add($"{nameof(RunId)} must not be blank.");
        }

        return errors;
    }

    private static string GenerateRunId()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RunIdBytes));
    }
}
