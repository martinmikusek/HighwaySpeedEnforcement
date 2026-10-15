using System.Net.Http;
using System.Windows;
using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.Infrastructure;
using HighwaySpeedEnforcement.Client.ViewModels;
using HighwaySpeedEnforcement.Client.Wpf.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HighwaySpeedEnforcement.Client.Wpf;

/// <summary>
/// Composition root (design section 3): configuration from <c>appsettings.json</c> section <c>Client</c>, environment
/// variables and the command line; logging to the debug and console providers; one long-lived <see cref="HttpClient"/>;
/// the poller loop on a background task that is cancelled on exit.
/// </summary>
public partial class App : System.Windows.Application
{
    public const int ExitCodeInvalidSettings = 1;

    private static readonly TimeSpan _shutdownWait = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private IHost? _host;
    private Task? _polling;

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder(e.Args);
        var settings = builder.Configuration.GetSection(ClientSettings.SectionName).Get<ClientSettings>() ?? new ClientSettings();
        var errors = settings.Validate();

        if (errors.Count > 0)
        {
            MessageBox.Show(
                "Invalid client settings:\n\n" + string.Join("\n", errors),
                "Highway Speed Enforcement",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(ExitCodeInvalidSettings);
            return;
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddConsole();

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(settings.BaseUrl) });
        builder.Services.AddSingleton<ISnapshotClient, HttpSnapshotClient>();
        builder.Services.AddSingleton<SnapshotPoller>();
        builder.Services.AddSingleton<IDashboardStateSource>(provider => provider.GetRequiredService<SnapshotPoller>());
        builder.Services.AddSingleton<IPollIntervalControl>(provider => provider.GetRequiredService<SnapshotPoller>());
        builder.Services.AddSingleton<IUiDispatcher>(new DispatcherUiDispatcher(Dispatcher));
        builder.Services.AddSingleton(DisplayFormatter.Local);
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        var poller = _host.Services.GetRequiredService<SnapshotPoller>();
        _polling = Task.Run(() => poller.RunAsync(_stop.Token), _stop.Token);

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stop.Cancel();
        _polling?.Wait(_shutdownWait);
        _host?.Services.GetRequiredService<MainViewModel>().Dispose();
        _host?.Services.GetRequiredService<HttpClient>().Dispose();
        _host?.Dispose();
        _stop.Dispose();

        base.OnExit(e);
    }
}
