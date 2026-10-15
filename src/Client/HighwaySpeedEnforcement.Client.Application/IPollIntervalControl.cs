namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>The polling period, changeable while the client runs (an extension of BR-UI-02: the default stays the assignment's 3 s).</summary>
public interface IPollIntervalControl
{
    TimeSpan Interval { get; }

    /// <summary>Takes effect at once: the next request follows one new period after the change.</summary>
    void SetInterval(TimeSpan interval);
}
