using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.ViewModels;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.ViewModels;

/// <summary>A state source the test drives by hand, as the poller would from its thread.</summary>
internal sealed class FakeStateSource : IDashboardStateSource
{
    public event EventHandler<DashboardState>? StateChanged;

    public DashboardState State { get; private set; } = DashboardState.Initial;

    public int SubscriberCount => StateChanged?.GetInvocationList().Length ?? 0;

    public void Raise(DashboardState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}

/// <summary>Runs the work immediately, as if the test thread were the UI thread.</summary>
internal sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        action();
    }
}

/// <summary>Holds the posted work until <see cref="RunAll"/>; proves that nothing changes outside the dispatcher.</summary>
internal sealed class RecordingDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _pending = new();

    public int PendingCount => _pending.Count;

    public void Post(Action action)
    {
        _pending.Enqueue(action);
    }

    public void RunAll()
    {
        while (_pending.Count > 0)
        {
            _pending.Dequeue()();
        }
    }
}

/// <summary>A polling period the test can read and a record of what the view model asked for.</summary>
internal sealed class FakePollIntervalControl : IPollIntervalControl
{
    public FakePollIntervalControl(TimeSpan interval)
    {
        Interval = interval;
    }

    public TimeSpan Interval { get; private set; }

    public List<TimeSpan> Requests { get; } = [];

    public void SetInterval(TimeSpan interval)
    {
        Requests.Add(interval);
        Interval = interval;
    }
}
