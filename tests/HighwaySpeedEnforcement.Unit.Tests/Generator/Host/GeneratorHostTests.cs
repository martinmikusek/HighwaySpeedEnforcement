using HighwaySpeedEnforcement.Generator;
using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using HighwaySpeedEnforcement.Generator.Interactive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

public sealed class GeneratorHostTests
{
    [Fact]
    public void Check_with_valid_settings_returns_exit_code_zero_and_the_bound_settings()
    {
        var configuration = Configuration(new Dictionary<string, string?> { ["Generator:Seed"] = "42", ["Generator:MaxTicks"] = "3" });

        var check = GeneratorHost.Check(configuration);

        Assert.Equal(0, check.ExitCode);
        Assert.Empty(check.Errors);
        Assert.Equal(42, check.Settings.Seed);
        Assert.Equal(3, check.Settings.MaxTicks);
    }

    [Fact]
    public void Check_without_a_generator_section_uses_the_defaults()
    {
        var check = GeneratorHost.Check(Configuration([]));

        Assert.Equal(0, check.ExitCode);
        Assert.Equal(100, check.Settings.VehiclesPerTick);
    }

    [Fact]
    public void Check_with_invalid_settings_returns_every_error_and_exit_code_one()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Generator:VehiclesPerTick"] = "0",
            ["Generator:MaxTicks"] = "0",
            ["Generator:Mqtt:Port"] = "70000",
        });

        var check = GeneratorHost.Check(configuration);

        Assert.Equal(GeneratorHost.ExitCodeInvalidSettings, check.ExitCode);
        Assert.Equal(3, check.Errors.Count);
        Assert.Contains(check.Errors, error => error.StartsWith("VehiclesPerTick", StringComparison.Ordinal));
        Assert.Contains(check.Errors, error => error.StartsWith("MaxTicks", StringComparison.Ordinal));
        Assert.Contains(check.Errors, error => error.StartsWith("Mqtt:Port", StringComparison.Ordinal));
    }

    [Fact]
    public void Configure_builds_a_host_that_resolves_the_runner_the_publisher_and_the_store()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = ["--Generator:Seed=42"] });
        var check = GeneratorHost.Check(builder.Configuration);
        GeneratorHost.Configure(builder, check.Settings);

        using var host = builder.Build();

        Assert.Single(host.Services.GetServices<IHostedService>().OfType<SimulationRunner>());
        Assert.IsType<MqttMeasurementPublisher>(host.Services.GetRequiredService<IMeasurementPublisher>());
        Assert.IsType<FilePlateStateStore>(host.Services.GetRequiredService<IPlateStateStore>());
        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
        Assert.Same(check.Settings, host.Services.GetRequiredService<GeneratorSettings>());
        Assert.Same(host.Services.GetRequiredService<RuntimeControl>(), host.Services.GetRequiredService<RuntimeControl>());
    }

    [Fact]
    public void Configure_with_a_redirected_terminal_keeps_the_json_console_logging_and_no_console_service()
    {
        using var host = Build(new FakeTerminal { IsOutputRedirected = true }, []);

        var providers = host.Services.GetServices<ILoggerProvider>().ToArray();
        Assert.Single(providers.OfType<ConsoleLoggerProvider>());
        Assert.DoesNotContain(providers, provider => provider is CompactConsoleLoggerProvider);
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service => service is InteractiveConsoleService);
        Assert.False(host.Services.GetRequiredService<InteractiveConsole>().IsActive);
    }

    [Fact]
    public void Configure_with_a_real_terminal_switches_to_the_compact_logging_and_adds_the_console_service_first()
    {
        using var host = Build(new FakeTerminal(), []);

        var providers = host.Services.GetServices<ILoggerProvider>().ToArray();
        Assert.Single(providers.OfType<CompactConsoleLoggerProvider>());
        Assert.DoesNotContain(providers, provider => provider is ConsoleLoggerProvider);
        var services = host.Services.GetServices<IHostedService>().ToArray();
        Assert.IsType<InteractiveConsoleService>(services[0]);
        Assert.IsType<SimulationRunner>(services[1]);
        Assert.True(host.Services.GetRequiredService<InteractiveConsole>().IsActive);
    }

    [Fact]
    public void Configure_with_the_setting_off_stays_non_interactive_even_on_a_real_terminal()
    {
        using var host = Build(new FakeTerminal(), ["--Generator:Console:Interactive=false"]);

        Assert.Single(host.Services.GetServices<ILoggerProvider>().OfType<ConsoleLoggerProvider>());
        Assert.False(host.Services.GetRequiredService<InteractiveConsole>().IsActive);
    }

    [Fact]
    public void Default_state_path_is_under_local_application_data()
    {
        var check = GeneratorHost.Check(Configuration([]));
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localApplicationData, check.Settings.StatePath, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("HighwaySpeedEnforcement", "generator-state.json"), check.Settings.StatePath, StringComparison.Ordinal);
    }

    private static IHost Build(FakeTerminal terminal, string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = ["--Generator:Seed=1", .. args] });
        var check = GeneratorHost.Check(builder.Configuration);
        Assert.Equal(0, check.ExitCode);
        GeneratorHost.Configure(builder, check.Settings, terminal);

        return builder.Build();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
