using System.Globalization;
using HighwaySpeedEnforcement.Generator.Interactive;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

public sealed class StatusLineFormatterTests
{
    private static readonly StatusLineState Example = new(
        Tick: 123,
        TrafficPaused: false,
        VehiclesPerTick: 100,
        HeartbeatsEnabled: false,
        DisabledCameras: [7],
        Broker: BrokerState.Connected,
        MessagesSent: 12_345,
        LastTickDuration: TimeSpan.FromMilliseconds(130),
        Seed: 42);

    [Fact]
    public void FormatStatus_matches_the_example_of_the_design()
    {
        string line = StatusLineFormatter.FormatStatus(Example, 200);

        Assert.Equal("tick 123 | traffic RUNNING | 100 veh/tick | heartbeats off | cameras off: 7 | broker connected | sent 12,345 | last tick 0.13 s | seed 42", line);
    }

    [Fact]
    public void FormatStatus_shows_paused_heartbeats_reconnecting_and_a_random_seed()
    {
        var state = Example with { TrafficPaused = true, HeartbeatsEnabled = true, DisabledCameras = [], Broker = BrokerState.Reconnecting, Seed = null, VehiclesPerTick = 2_000, MessagesSent = 1_234_567, LastTickDuration = TimeSpan.FromSeconds(1.5) };

        string line = StatusLineFormatter.FormatStatus(state, 200);

        Assert.Equal("tick 123 | traffic PAUSED | 2,000 veh/tick | heartbeats on | cameras off: none | broker reconnecting | sent 1,234,567 | last tick 1.50 s SLOW | seed random", line);
    }

    [Fact]
    public void FormatStatus_shows_connecting_before_the_first_connection()
    {
        Assert.Contains("broker connecting", StatusLineFormatter.FormatStatus(Example with { Broker = BrokerState.Connecting }, 200), StringComparison.Ordinal);
    }

    [Fact]
    public void FormatStatus_uses_the_invariant_culture_whatever_the_thread_culture_is()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");

        try
        {
            string line = StatusLineFormatter.FormatStatus(Example with { MessagesSent = 1_234_567, LastTickDuration = TimeSpan.FromMilliseconds(130) }, 200);

            Assert.Contains("sent 1,234,567", line, StringComparison.Ordinal);
            Assert.Contains("last tick 0.13 s", line, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(120)]
    [InlineData(200)]
    public void Lines_never_exceed_the_width_and_are_cut_with_an_ellipsis(int width)
    {
        var state = Example with { MessagesSent = 123_456_789 };
        string status = StatusLineFormatter.FormatStatus(state, width);
        string help = StatusLineFormatter.FormatKeyHelp(width);

        Assert.True(status.Length <= width, $"status is {status.Length} wide at width {width}");
        Assert.True(help.Length <= width, $"help is {help.Length} wide at width {width}");
        Assert.DoesNotContain('\u001b', status);
        Assert.DoesNotContain('\u001b', help);

        if (width >= 100)
        {
            Assert.Contains("last tick 0.13 s", status, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(width, status.Length);
            Assert.EndsWith(StatusLineFormatter.Ellipsis, status, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_narrow_terminal_drops_the_least_important_parts_first_and_keeps_the_tick_duration()
    {
        var state = Example with { DisabledCameras = [], MessagesSent = 97_600, VehiclesPerTick = 2_000, LastTickDuration = TimeSpan.FromSeconds(2.76), Tick = 36 };
        string full = StatusLineFormatter.FormatStatus(state, int.MaxValue);

        string withoutSeed = StatusLineFormatter.FormatStatus(state, full.Length - 1);
        string at119 = StatusLineFormatter.FormatStatus(state, 119);
        string at100 = StatusLineFormatter.FormatStatus(state, 100);

        Assert.Contains("seed 42", full, StringComparison.Ordinal);
        Assert.DoesNotContain("seed", withoutSeed, StringComparison.Ordinal);
        Assert.Contains("sent 97,600", withoutSeed, StringComparison.Ordinal);
        Assert.DoesNotContain("seed", at119, StringComparison.Ordinal);
        Assert.DoesNotContain("sent", at119, StringComparison.Ordinal);
        Assert.Contains("last tick 2.76 s SLOW", at119, StringComparison.Ordinal);
        Assert.Contains("last tick 2.76 s SLOW", at100, StringComparison.Ordinal);
        Assert.Contains("broker connected", at100, StringComparison.Ordinal);
        Assert.True(at119.Length <= 119);
        Assert.True(at100.Length <= 100);
    }

    [Fact]
    public void Disabled_cameras_are_kept_longer_than_the_other_optional_parts()
    {
        var state = Example with { DisabledCameras = [3, 7], MessagesSent = 97_600 };

        string line = StatusLineFormatter.FormatStatus(state, 90);

        Assert.Contains("cameras off: 3,7", line, StringComparison.Ordinal);
        Assert.DoesNotContain("seed", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.99, false)]
    [InlineData(1.0, false)]
    [InlineData(1.01, true)]
    [InlineData(2.76, true)]
    public void A_tick_longer_than_the_interval_is_marked_slow(double seconds, bool slow)
    {
        string line = StatusLineFormatter.FormatStatus(Example with { LastTickDuration = TimeSpan.FromSeconds(seconds) }, 200);

        Assert.Equal(slow, line.Contains("SLOW", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatKeyHelp_lists_every_key()
    {
        string help = StatusLineFormatter.FormatKeyHelp(200);

        Assert.All(new[] { "[P]", "[H]", "[+]", "[-]", "[1-9,0]", "[Q]" }, key => Assert.Contains(key, help, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new int[0], "none")]
    [InlineData(new[] { 7 }, "7")]
    [InlineData(new[] { 3, 7 }, "3,7")]
    [InlineData(new[] { 1, 10 }, "1,10")]
    public void FormatCameras_lists_the_cameras_that_are_off(int[] cameras, string expected)
    {
        Assert.Equal(expected, StatusLineFormatter.FormatCameras(cameras));
    }

    [Theory]
    [InlineData("abcdef", 6, "abcdef")]
    [InlineData("abcdef", 5, "ab...")]
    [InlineData("abcdef", 3, "...")]
    [InlineData("abcdef", 2, "..")]
    [InlineData("abcdef", 0, "")]
    [InlineData("", 5, "")]
    public void Fit_cuts_to_the_width(string text, int width, string expected)
    {
        Assert.Equal(expected, StatusLineFormatter.Fit(text, width));
    }
}
