using System.Globalization;
using System.Text;

namespace HighwaySpeedEnforcement.Generator.Interactive;

public enum BrokerState
{
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>Everything the status line shows (design section 11).</summary>
public sealed record StatusLineState(
    long Tick,
    bool TrafficPaused,
    int VehiclesPerTick,
    bool HeartbeatsEnabled,
    IReadOnlyList<int> DisabledCameras,
    BrokerState Broker,
    long MessagesSent,
    TimeSpan LastTickDuration,
    long? Seed);

/// <summary>
/// Produces the status line and the key-help line of design section 11 for a given terminal width: plain text, never
/// wider than the width, truncated with an ellipsis, numbers in the invariant culture.
/// </summary>
public static class StatusLineFormatter
{
    public const string Ellipsis = "...";

    /// <summary>The tick interval: a tick that takes longer is shown with a SLOW marker.</summary>
    private static readonly TimeSpan _tickBudget = TimeSpan.FromSeconds(1);

    public static string FormatStatus(StatusLineState state, int width)
    {
        ArgumentNullException.ThrowIfNull(state);

        string lastTick = state.LastTickDuration.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

        if (state.LastTickDuration > _tickBudget)
        {
            lastTick += " SLOW";
        }

        // Segments in display order with a keep priority: when the line is wider than the terminal the least important segments
        // (the highest number) are dropped first, instead of cutting the end of the line (which would hide the tick duration).
        var segments = new List<(string Text, int DropOrder, bool Droppable)>
        {
            ("tick " + state.Tick.ToString("N0", CultureInfo.InvariantCulture), 0, false),
            ("traffic " + (state.TrafficPaused ? "PAUSED" : "RUNNING"), 0, false),
            (state.VehiclesPerTick.ToString("N0", CultureInfo.InvariantCulture) + " veh/tick", 0, false),
            ("heartbeats " + (state.HeartbeatsEnabled ? "on" : "off"), 2, true),
            ("cameras off: " + FormatCameras(state.DisabledCameras), state.DisabledCameras.Count == 0 ? 3 : 0, state.DisabledCameras.Count == 0),
            ("broker " + FormatBroker(state.Broker), 0, false),
            ("sent " + state.MessagesSent.ToString("N0", CultureInfo.InvariantCulture), 4, true),
            ("last tick " + lastTick, 0, false),
            ("seed " + (state.Seed?.ToString(CultureInfo.InvariantCulture) ?? "random"), 5, true),
        };

        while (Join(segments).Length > width)
        {
            int victim = -1;

            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].Droppable && (victim < 0 || segments[i].DropOrder >= segments[victim].DropOrder))
                {
                    victim = i;
                }
            }

            if (victim < 0)
            {
                break;
            }

            segments.RemoveAt(victim);
        }

        return Fit(Join(segments), width);
    }

    private static string Join(List<(string Text, int DropOrder, bool Droppable)> segments)
    {
        return string.Join(" | ", segments.Select(segment => segment.Text));
    }

    public static string FormatKeyHelp(int width)
    {
        return Fit("[P] pause/resume traffic  [H] heartbeats on/off  [+]/[-] vehicles +/-10 (+/-100 over 100)  [1-9,0] camera 1-10 on/off  [Q] quit", width);
    }

    public static string FormatCameras(IReadOnlyList<int> disabledCameras)
    {
        ArgumentNullException.ThrowIfNull(disabledCameras);

        return disabledCameras.Count == 0 ? "none" : string.Join(",", disabledCameras.Select(camera => camera.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The text cut to at most <paramref name="width"/> characters, ending with the ellipsis when it was cut.</summary>
    public static string Fit(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(width);

        if (text.Length <= width)
        {
            return text;
        }

        return width <= Ellipsis.Length ? Ellipsis[..width] : string.Concat(text.AsSpan(0, width - Ellipsis.Length), Ellipsis);
    }

    private static string FormatBroker(BrokerState state)
    {
        return state switch
        {
            BrokerState.Connected => "connected",
            BrokerState.Reconnecting => "reconnecting",
            _ => "connecting",
        };
    }
}
