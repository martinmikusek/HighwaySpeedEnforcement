using System.Globalization;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Infrastructure;

namespace HighwaySpeedEnforcement.Processor.Api.Configuration;

/// <summary>The bound and validated host settings; <see cref="Errors"/> is empty when the host may start.</summary>
public sealed record ProcessorHostSettings(ProcessorSettings Processor, MqttSourceOptions Mqtt, IReadOnlyList<string> Errors);

/// <summary>
/// Binds the <c>Processor</c> and <c>Mqtt</c> sections (design section 6). Durations are <c>hh:mm:ss</c> or the word
/// <c>Infinite</c>; the default binder knows no infinity, so the processor section is read by hand. Every problem is
/// collected into one list so that a misconfigured host reports all of them at once.
/// </summary>
public static class ProcessorConfiguration
{
    public const string ProcessorSection = "Processor";
    public const string MqttSection = "Mqtt";
    public const string InfiniteKeyword = "Infinite";

    public static ProcessorHostSettings Load(IConfiguration configuration)
    {
        var errors = new List<string>();
        var processor = BindProcessor(configuration.GetSection(ProcessorSection), errors);
        var mqtt = BindMqtt(configuration.GetSection(MqttSection), errors);

        errors.AddRange(processor.Validate().Select(error => $"{ProcessorSection}: {error}"));
        errors.AddRange(mqtt.Validate().Select(error => $"{MqttSection}: {error}"));

        return new ProcessorHostSettings(processor, mqtt, errors);
    }

    /// <summary>The text form of a duration as the configuration expects it; the inverse of the binding.</summary>
    public static string FormatDuration(TimeSpan value)
    {
        return value == Timeout.InfiniteTimeSpan ? InfiniteKeyword : value.ToString("c", CultureInfo.InvariantCulture);
    }

    private static ProcessorSettings BindProcessor(IConfigurationSection section, List<string> errors)
    {
        var settings = new ProcessorSettings();
        settings.PartitionCount = ReadInt(section, nameof(settings.PartitionCount), settings.PartitionCount, errors);
        settings.IngestChannelCapacity = ReadInt(section, nameof(settings.IngestChannelCapacity), settings.IngestChannelCapacity, errors);
        settings.CameraBoardWindow = ReadDuration(section, nameof(settings.CameraBoardWindow), settings.CameraBoardWindow, errors);
        settings.GlobalBoardLinger = ReadDuration(section, nameof(settings.GlobalBoardLinger), settings.GlobalBoardLinger, errors);
        settings.InactivityTimeout = ReadDuration(section, nameof(settings.InactivityTimeout), settings.InactivityTimeout, errors);
        settings.FutureTimestampTolerance = ReadDuration(section, nameof(settings.FutureTimestampTolerance), settings.FutureTimestampTolerance, errors);
        settings.IngestLagWarning = ReadDuration(section, nameof(settings.IngestLagWarning), settings.IngestLagWarning, errors);
        settings.HeartbeatsEnabled = ReadBool(section, nameof(settings.HeartbeatsEnabled), settings.HeartbeatsEnabled, errors);
        return settings;
    }

    private static MqttSourceOptions BindMqtt(IConfigurationSection section, List<string> errors)
    {
        var options = new MqttSourceOptions();

        try
        {
            section.Bind(options);
        }
        catch (InvalidOperationException exception)
        {
            errors.Add($"{MqttSection}: {exception.Message}");
        }

        return options;
    }

    private static int ReadInt(IConfigurationSection section, string key, int fallback, List<string> errors)
    {
        var text = section[key];

        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add($"{section.Path}:{key} must be an integer (was '{text}').");
        return fallback;
    }

    private static bool ReadBool(IConfigurationSection section, string key, bool fallback, List<string> errors)
    {
        var text = section[key];

        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (bool.TryParse(text, out var value))
        {
            return value;
        }

        errors.Add($"{section.Path}:{key} must be true or false (was '{text}').");
        return fallback;
    }

    private static TimeSpan ReadDuration(IConfigurationSection section, string key, TimeSpan fallback, List<string> errors)
    {
        var text = section[key];

        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (string.Equals(text.Trim(), InfiniteKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return Timeout.InfiniteTimeSpan;
        }

        if (TimeSpan.TryParseExact(text.Trim(), "c", CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add($"{section.Path}:{key} must be a duration such as 00:00:02 or the word {InfiniteKeyword} (was '{text}').");
        return fallback;
    }
}
