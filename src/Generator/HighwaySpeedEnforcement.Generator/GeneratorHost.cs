using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using HighwaySpeedEnforcement.Generator.Interactive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HighwaySpeedEnforcement.Generator;

/// <summary>Composition root of the generator (design section 3): settings check, logging and the services of the Generic Host.</summary>
public static class GeneratorHost
{
    public const int ExitCodeInvalidSettings = 1;

    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Binds and validates the settings; the exit code is non-zero when the process must not start.</summary>
    public static StartupCheck Check(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection(GeneratorSettings.SectionName).Get<GeneratorSettings>() ?? new GeneratorSettings();
        var errors = settings.Validate();

        return new StartupCheck(settings, errors, errors.Count == 0 ? 0 : ExitCodeInvalidSettings);
    }

    /// <summary>Registers everything the runner needs for the process's own terminal; the settings must have passed <see cref="Check"/>.</summary>
    public static void Configure(HostApplicationBuilder builder, GeneratorSettings settings)
    {
        Configure(builder, settings, InteractiveConsole.SystemTerminal);
    }

    /// <summary>
    /// As <see cref="Configure(HostApplicationBuilder, GeneratorSettings)"/>, for the given terminal. When the interactive console
    /// is active (design section 11) the JSON console logging is replaced by the compact one-line format; otherwise nothing differs
    /// from a non-interactive run.
    /// </summary>
    public static void Configure(HostApplicationBuilder builder, GeneratorSettings settings, IConsoleTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(terminal);

        var control = new RuntimeControl(settings.ToSimulationOptions());
        var status = new RunnerStatus();
        var console = new InteractiveConsole(terminal, control, status, settings.Console.Interactive);

        builder.Logging.ClearProviders();

        if (console.IsActive)
        {
            builder.Logging.AddProvider(new CompactConsoleLoggerProvider(console, TimeProvider.System));
        }
        else
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "O";
            });
        }

        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(control);
        builder.Services.AddSingleton(status);
        builder.Services.AddSingleton(console);
        builder.Services.AddSingleton<IPlateStateStore>(provider =>
            new FilePlateStateStore(settings.StatePath, provider.GetRequiredService<ILogger<FilePlateStateStore>>()));
        builder.Services.AddSingleton<IMeasurementPublisher>(provider =>
            new MqttMeasurementPublisher(settings.Mqtt, provider.GetRequiredService<ILogger<MqttMeasurementPublisher>>(), provider.GetRequiredService<TimeProvider>()));

        if (console.IsActive)
        {
            // Before the runner: the bar is on screen before the first tick, and the terminal is restored after the last one.
            builder.Services.AddHostedService<InteractiveConsoleService>();
        }

        builder.Services.AddHostedService<SimulationRunner>();
    }

    public sealed record StartupCheck(GeneratorSettings Settings, IReadOnlyList<string> Errors, int ExitCode);
}
