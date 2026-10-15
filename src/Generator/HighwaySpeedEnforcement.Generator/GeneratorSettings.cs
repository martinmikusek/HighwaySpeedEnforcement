using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using HighwaySpeedEnforcement.Generator.Interactive;

namespace HighwaySpeedEnforcement.Generator;

/// <summary>
/// Section <c>Generator</c> of the configuration (design section 6). A plain object bound by the host; <see cref="Validate"/>
/// reports every violated rule before the host starts.
/// </summary>
public sealed class GeneratorSettings
{
    public const string SectionName = "Generator";

    /// <summary>When set: a reproducible run and the state file is not used. When absent: a random seed is drawn and logged.</summary>
    public long? Seed { get; set; }

    public string StatePath { get; set; } = DefaultStatePath();

    public int VehiclesPerTick { get; set; } = 100;

    /// <summary>After this tick no new vehicles spawn; the active ones drain.</summary>
    public long? SpawnUntilTick { get; set; }

    /// <summary>The process exits with code 0 after this tick; absent means run until stopped.</summary>
    public long? MaxTicks { get; set; }

    public List<int> DisabledCameras { get; set; } = [];

    public HeartbeatSettings Heartbeats { get; set; } = new();

    public SpeedSettings Speed { get; set; } = new();

    public MqttPublisherOptions Mqtt { get; set; } = new();

    public ConsoleSettings Console { get; set; } = new();

    public static string DefaultStatePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HighwaySpeedEnforcement",
            "generator-state.json");
    }

    /// <summary>Every violated rule as one message, the MQTT ones prefixed with <c>Mqtt:</c>; empty when the settings are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (VehiclesPerTick < 1)
        {
            errors.Add($"{nameof(VehiclesPerTick)} must be at least 1 (was {VehiclesPerTick}).");
        }

        var outside = DisabledCameras.Where(camera => camera is < 1 or > Simulation.CameraCount).ToArray();
        if (outside.Length > 0)
        {
            errors.Add($"{nameof(DisabledCameras)} must be within 1 to {Simulation.CameraCount} (was {string.Join(", ", outside)}).");
        }

        var duplicates = DisabledCameras.GroupBy(camera => camera).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
        {
            errors.Add($"{nameof(DisabledCameras)} must not contain duplicates (was {string.Join(", ", duplicates)}).");
        }

        if (SpawnUntilTick is < 1)
        {
            errors.Add($"{nameof(SpawnUntilTick)} must be positive when set (was {SpawnUntilTick}).");
        }

        if (MaxTicks is < 1)
        {
            errors.Add($"{nameof(MaxTicks)} must be positive when set (was {MaxTicks}).");
        }

        if (Seed is null && string.IsNullOrWhiteSpace(StatePath))
        {
            errors.Add($"{nameof(StatePath)} must not be blank when no {nameof(Seed)} is set.");
        }

        errors.AddRange(Speed.Validate().Select(error => $"{nameof(Speed)}:{error}"));
        errors.AddRange(Mqtt.Validate().Select(error => $"{nameof(Mqtt)}:{error}"));

        return errors;
    }

    public SimulationOptions ToSimulationOptions()
    {
        return new SimulationOptions
        {
            VehiclesPerTick = VehiclesPerTick,
            SpawnUntilTick = SpawnUntilTick,
            DisabledCameras = DisabledCameras.ToArray(),
            HeartbeatsEnabled = Heartbeats.Enabled,
            Speed = new SpeedOptions
            {
                BaselineKmh = Speed.BaselineKmh,
                DesiredStdDev = Speed.DesiredStdDev,
                StepStdDev = Speed.StepStdDev,
            },
        };
    }

    public sealed class HeartbeatSettings
    {
        public bool Enabled { get; set; }
    }

    /// <summary>Section <c>Generator:Console</c> (design section 11).</summary>
    public sealed class ConsoleSettings
    {
        /// <summary>
        /// Whether the interactive console is wanted (default). It is used only when standard input and output are a real terminal,
        /// so redirected runs (tests, <c>&gt; file</c>, containers) stay non-interactive whatever this says; <c>false</c> turns it off.
        /// </summary>
        public bool Interactive { get; set; } = true;
    }

    public sealed class SpeedSettings
    {
        public double BaselineKmh { get; set; } = 130.0;

        public double DesiredStdDev { get; set; } = 15.0;

        public double StepStdDev { get; set; } = 3.0;

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (!(BaselineKmh > 0))
            {
                errors.Add($"{nameof(BaselineKmh)} must be positive (was {BaselineKmh}).");
            }

            if (!(DesiredStdDev > 0))
            {
                errors.Add($"{nameof(DesiredStdDev)} must be positive (was {DesiredStdDev}).");
            }

            if (!(StepStdDev > 0))
            {
                errors.Add($"{nameof(StepStdDev)} must be positive (was {StepStdDev}).");
            }

            return errors;
        }
    }
}
