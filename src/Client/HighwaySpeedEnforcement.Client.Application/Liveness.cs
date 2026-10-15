namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>The client's judgment of one board (BR-UI-04).</summary>
public enum Liveness
{
    /// <summary>The board never received data (no watermark).</summary>
    NoData,

    /// <summary>The lag is below <c>LiveLagThreshold</c>.</summary>
    Live,

    /// <summary>The board is frozen or late: the lag is at or above <c>LiveLagThreshold</c>.</summary>
    NotLive,
}
