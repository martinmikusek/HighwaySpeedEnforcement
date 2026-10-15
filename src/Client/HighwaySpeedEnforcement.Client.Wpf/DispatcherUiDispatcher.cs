using System.Windows.Threading;
using HighwaySpeedEnforcement.Client.ViewModels;

namespace HighwaySpeedEnforcement.Client.Wpf;

/// <summary>Adapter of <see cref="IUiDispatcher"/> over the WPF <see cref="Dispatcher"/>; the work is queued, never run inline.</summary>
public sealed class DispatcherUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public DispatcherUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Post(Action action)
    {
        _dispatcher.BeginInvoke(action);
    }
}
