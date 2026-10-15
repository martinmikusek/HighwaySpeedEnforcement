using HighwaySpeedEnforcement.Generator;
using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Interactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

public sealed class InteractiveConsoleTests
{
    private const string Esc = "\u001b";

    private readonly RuntimeControl _control = new(new SimulationOptions());
    private readonly RunnerStatus _status = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, false)]
    public void ShouldActivate_needs_a_terminal_on_both_ends_and_the_setting_not_off(bool inputRedirected, bool outputRedirected, bool enabled, bool expected)
    {
        var terminal = new FakeTerminal { IsInputRedirected = inputRedirected, IsOutputRedirected = outputRedirected };

        Assert.Equal(expected, InteractiveConsole.ShouldActivate(terminal, enabled));
        using var console = new InteractiveConsole(terminal, _control, _status, enabled);
        Assert.Equal(expected, console.IsActive);
    }

    [Fact]
    public void Inactive_console_touches_the_terminal_only_for_plain_lines()
    {
        var terminal = new FakeTerminal { IsOutputRedirected = true };
        using var console = new InteractiveConsole(terminal, _control, _status, true);

        console.Start(static () => BrokerState.Connected, static () => { });
        console.WriteLine("hello");
        _status.RecordTick(1, 100, TimeSpan.FromMilliseconds(10));
        console.Dispose();

        Assert.Equal("hello" + Environment.NewLine, terminal.Written);
        Assert.Equal(0, terminal.EnableAnsiCalls);
        Assert.False(console.IsBarShown);
    }

    [Fact]
    public void Start_sets_the_scroll_region_hides_the_cursor_and_draws_the_bar()
    {
        var terminal = new FakeTerminal { Width = 160, Height = 30 };
        using var console = new InteractiveConsole(terminal, _control, _status, true);
        _status.SetSeed(42);

        console.Start(static () => BrokerState.Connecting, static () => { });

        string written = terminal.Written;
        Assert.True(console.IsBarShown);
        Assert.StartsWith($"{Esc}[?25l{Esc}[1;27r{Esc}[27;1H", written, StringComparison.Ordinal);
        Assert.Contains($"{Esc}7", written, StringComparison.Ordinal);
        Assert.Contains($"{Esc}[28;1H{Esc}[2K" + new string('-', 159) + Esc, written, StringComparison.Ordinal);
        Assert.Contains($"{Esc}[29;1H{Esc}[2Ktick 0 | traffic RUNNING | 100 veh/tick | heartbeats off | cameras off: none | broker connecting | sent 0 | last tick 0.00 s | seed 42", written, StringComparison.Ordinal);
        Assert.Contains($"{Esc}[30;1H{Esc}[2K[P] pause/resume traffic", written, StringComparison.Ordinal);
        Assert.EndsWith($"{Esc}8", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Narrow_terminal_gets_truncated_lines_that_never_exceed_the_width()
    {
        var terminal = new FakeTerminal { Width = 60, Height = 20 };
        using var console = new InteractiveConsole(terminal, _control, _status, true);

        console.Start(static () => BrokerState.Connected, static () => { });

        var rows = terminal.Written.Split(Esc).Where(part => part.StartsWith("[2K", StringComparison.Ordinal)).Select(part => part[3..]).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Equal(59, row.Length));
        Assert.EndsWith(StatusLineFormatter.Ellipsis, rows[1], StringComparison.Ordinal);
        Assert.EndsWith(StatusLineFormatter.Ellipsis, rows[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Resize_resets_the_screen_for_the_new_size()
    {
        var terminal = new FakeTerminal { Width = 160, Height = 30 };
        using var console = new InteractiveConsole(terminal, _control, _status, true);
        console.Start(static () => BrokerState.Connected, static () => { });
        terminal.ClearWritten();

        terminal.Height = 24;
        SpinWait.SpinUntil(() => terminal.Written.Length > 0, TimeSpan.FromSeconds(10));

        Assert.StartsWith($"{Esc}[r{Esc}[2J{Esc}[H{Esc}[?25l{Esc}[1;21r{Esc}[21;1H", terminal.Written, StringComparison.Ordinal);
        Assert.Contains($"{Esc}[24;1H{Esc}[2K[P]", terminal.Written, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_restores_the_terminal()
    {
        var terminal = new FakeTerminal { Width = 160, Height = 30 };
        var console = new InteractiveConsole(terminal, _control, _status, true);
        console.Start(static () => BrokerState.Connected, static () => { });
        terminal.ClearWritten();

        console.Dispose();

        Assert.Equal($"{Esc}[r{Esc}[30;1H\r\n{Esc}[?25h", terminal.Written);
        Assert.False(console.IsBarShown);

        console.Dispose();
        Assert.Equal($"{Esc}[r{Esc}[30;1H\r\n{Esc}[?25h", terminal.Written);
    }

    [Fact]
    public void Without_ansi_support_no_escape_sequence_is_written_and_keys_still_work()
    {
        var terminal = new FakeTerminal { AnsiSupported = false };
        using var console = new InteractiveConsole(terminal, _control, _status, true);

        console.Start(static () => BrokerState.Connected, static () => { });
        console.WriteLine("line one");
        _status.RecordTick(1, 100, TimeSpan.FromMilliseconds(10));
        console.HandleKey(Key('p'));
        console.RedrawBar();
        console.Dispose();

        Assert.Equal(1, terminal.EnableAnsiCalls);
        Assert.False(console.IsBarShown);
        Assert.DoesNotContain(Esc, terminal.Written, StringComparison.Ordinal);
        Assert.Equal("line one" + Environment.NewLine, terminal.Written);
        Assert.True(_control.Snapshot().TrafficPaused);
    }

    [Fact]
    public void WriteLine_above_the_bar_ends_with_a_carriage_return_and_line_feed()
    {
        var terminal = new FakeTerminal();
        using var console = new InteractiveConsole(terminal, _control, _status, true);
        console.Start(static () => BrokerState.Connected, static () => { });
        terminal.ClearWritten();

        console.WriteLine("12:00:00 INF hello");

        Assert.Equal("12:00:00 INF hello\r\n", terminal.Written);
    }

    [Fact]
    public void Bar_is_redrawn_when_the_runner_reports_a_tick()
    {
        var terminal = new FakeTerminal();
        using var console = new InteractiveConsole(terminal, _control, _status, true);
        console.Start(static () => BrokerState.Connected, static () => { });
        terminal.ClearWritten();

        _status.RecordTick(7, 12_345, TimeSpan.FromMilliseconds(130));

        Assert.Contains("tick 7 | traffic RUNNING | 100 veh/tick | heartbeats off | cameras off: none | broker connected | sent 12,345 | last tick 0.13 s | seed random", terminal.Written, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKey_writes_the_control_and_redraws_the_bar()
    {
        var terminal = new FakeTerminal();
        using var console = new InteractiveConsole(terminal, _control, _status, true);
        bool quit = false;
        console.Start(static () => BrokerState.Connected, () => quit = true);
        terminal.ClearWritten();

        console.HandleKey(Key('P'));
        console.HandleKey(Key('h'));
        console.HandleKey(Key('+'));
        console.HandleKey(Key('+'));
        console.HandleKey(Key('-'));
        console.HandleKey(Key('7'));
        console.HandleKey(Key('0'));
        console.HandleKey(Key('x'));

        Assert.Equal(new RuntimeControlSnapshot(true, 200, true, [7, 10]), _control.Snapshot());
        Assert.Contains("traffic PAUSED | 200 veh/tick | heartbeats on | cameras off: 7,10", terminal.Written, StringComparison.Ordinal);
        Assert.False(quit);

        console.HandleKey(Key('q'));
        Assert.True(quit);
    }

    [Fact]
    public async Task Key_loop_reads_queued_keys_and_q_quits()
    {
        var terminal = new FakeTerminal();
        using var console = new InteractiveConsole(terminal, _control, _status, true);
        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.Press('p');
        terminal.Press('3');
        terminal.Press('q');

        console.Start(static () => BrokerState.Connected, () => quit.TrySetResult());

        await quit.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True(_control.Snapshot().TrafficPaused);
        Assert.Equal([3], _control.Snapshot().DisabledCameras);
    }

    [Fact]
    public void Compact_logger_writes_one_line_per_record_through_the_console()
    {
        var terminal = new FakeTerminal();
        using var console = new InteractiveConsole(terminal, _control, _status, true);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 9, 30, 15, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        using var provider = new CompactConsoleLoggerProvider(console, time);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("started {Count} things", 3);
        logger.LogWarning(new InvalidOperationException("boom"), "failed");

        var lines = terminal.Written.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("09:30:15 INF started 3 things", lines[0]);
        Assert.Equal("09:30:15 WRN failed", lines[1]);
        Assert.Contains("InvalidOperationException: boom", lines[2], StringComparison.Ordinal);
        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    private static ConsoleKeyInfo Key(char keyChar)
    {
        return new ConsoleKeyInfo(keyChar, ConsoleKey.NoName, shift: false, alt: false, control: false);
    }
}
