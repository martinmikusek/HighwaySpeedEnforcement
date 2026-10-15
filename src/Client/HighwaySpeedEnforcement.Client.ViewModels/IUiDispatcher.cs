namespace HighwaySpeedEnforcement.Client.ViewModels;

/// <summary>Port: runs <paramref name="action"/> on the UI thread. Every change of a bound property happens inside it (design section 3).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
