using System.Runtime.InteropServices;
using System.Text;
using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Generator.Interactive;

/// <summary>
/// The interactive console of design section 11: keys change <see cref="RuntimeControl"/>, a three-row status bar is fixed at
/// the bottom of the terminal (ANSI scroll region) and log lines scroll above it. Every console write, from logging and from
/// the bar, goes through one lock. Inactive with redirected input or output or with the setting off; without ANSI support
/// the keys work and the lines are plain, without the bar. The only type that touches <c>System.Console</c>
/// (through <see cref="SystemTerminal"/>).
/// </summary>
public sealed partial class InteractiveConsole : IDisposable
{
    public const int BarRows = 3;
    public static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(50);

    private const string Escape = "\u001b";

    private readonly IConsoleTerminal _terminal;
    private readonly RuntimeControl _control;
    private readonly RunnerStatus _status;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stopping = new();

    private Func<BrokerState> _brokerState = static () => BrokerState.Connecting;
    private Action _quit = static () => { };
    private Task _keyLoop = Task.CompletedTask;
    private bool _barActive;
    private int _width;
    private int _height;
    private bool _disposed;

    public InteractiveConsole(IConsoleTerminal terminal, RuntimeControl control, RunnerStatus status, bool enabled)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        IsActive = ShouldActivate(terminal, enabled);
    }

    /// <summary>The real terminal of the process.</summary>
    public static IConsoleTerminal SystemTerminal { get; } = new SystemConsoleTerminal();

    /// <summary>Decided once at construction: a terminal on both ends and the setting not off.</summary>
    public bool IsActive { get; }

    /// <summary>True while the status bar is on screen (active, ANSI available, started and not yet disposed).</summary>
    public bool IsBarShown
    {
        get
        {
            lock (_lock)
            {
                return _barActive;
            }
        }
    }

    /// <summary>The activation rule of design section 11.</summary>
    public static bool ShouldActivate(IConsoleTerminal terminal, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        return enabled && !terminal.IsInputRedirected && !terminal.IsOutputRedirected;
    }

    /// <summary>Sets up the bar (when ANSI works) and starts reading keys; <paramref name="quit"/> is what <c>Q</c> does.</summary>
    public void Start(Func<BrokerState> brokerState, Action quit)
    {
        ArgumentNullException.ThrowIfNull(brokerState);
        ArgumentNullException.ThrowIfNull(quit);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsActive)
        {
            return;
        }

        _brokerState = brokerState;
        _quit = quit;

        if (_terminal.TryEnableAnsi())
        {
            lock (_lock)
            {
                _barActive = true;
                SetUpScreen();
            }
        }

        _status.Changed += RedrawBar;
        _keyLoop = Task.Run(() => ReadKeysAsync(_stopping.Token));
    }

    /// <summary>Writes one log line above the bar (or plainly when there is no bar); thread-safe.</summary>
    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_lock)
        {
            _terminal.Write(_barActive ? line + "\r\n" : line + Environment.NewLine);
        }
    }

    /// <summary>Applies one key; public so that the mapping to <see cref="RuntimeControl"/> is tested without the key loop.</summary>
    public void HandleKey(ConsoleKeyInfo key)
    {
        if (!KeyMap.TryMap(key, out var command))
        {
            return;
        }

        switch (command.Command)
        {
            case ConsoleCommand.TogglePause:
                _control.TogglePause();
                break;
            case ConsoleCommand.ToggleHeartbeats:
                _control.ToggleHeartbeats();
                break;
            case ConsoleCommand.IncreaseVehicles:
                _control.IncreaseVehicles();
                break;
            case ConsoleCommand.DecreaseVehicles:
                _control.DecreaseVehicles();
                break;
            case ConsoleCommand.ToggleCamera:
                _control.ToggleCamera(command.Camera);
                break;
            case ConsoleCommand.Quit:
                _quit();
                break;
        }

        RedrawBar();
    }

    /// <summary>Redraws the bar from the current state; a no-op without the bar.</summary>
    public void RedrawBar()
    {
        lock (_lock)
        {
            if (_barActive)
            {
                DrawBar();
            }
        }
    }

    /// <summary>Stops the key loop and restores the terminal (scroll region reset, cursor below the bar and visible).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _status.Changed -= RedrawBar;
        _stopping.Cancel();

        try
        {
            _keyLoop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The loop ends by cancellation; nothing else is expected.
        }

        lock (_lock)
        {
            if (_barActive)
            {
                RestoreScreen();
                _barActive = false;
            }
        }

        _stopping.Dispose();
    }

    private async Task ReadKeysAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_terminal.KeyAvailable)
                {
                    HandleKey(_terminal.ReadKey());
                    continue;
                }

                CheckResize();
                await Task.Delay(KeyPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                // The terminal went away (input closed); keys are over, the generator keeps running.
                return;
            }
        }
    }

    private void CheckResize()
    {
        lock (_lock)
        {
            if (_barActive && (_terminal.Width != _width || _terminal.Height != _height))
            {
                // Content under the old bar is unreliable after a resize; start the screen afresh.
                _terminal.Write(ResetScrollRegion() + $"{Escape}[2J{Escape}[H");
                SetUpScreen();
            }
        }
    }

    private void SetUpScreen()
    {
        _width = _terminal.Width;
        _height = _terminal.Height;
        int scrollBottom = Math.Max(1, _height - BarRows);

        // Hide the cursor, fix the bottom rows, park the cursor at the bottom of the scrolling part.
        _terminal.Write($"{Escape}[?25l{Escape}[1;{scrollBottom}r{Escape}[{scrollBottom};1H");
        DrawBar();
    }

    private void DrawBar()
    {
        int width = Math.Max(0, _width - 1);
        var values = _status.Current;
        var control = _control.Snapshot();
        var state = new StatusLineState(
            values.Tick,
            control.TrafficPaused,
            control.VehiclesPerTick,
            control.HeartbeatsEnabled,
            control.DisabledCameras,
            _brokerState(),
            values.MessagesSent,
            values.LastTickDuration,
            values.Seed);

        var text = new StringBuilder()
            .Append(Escape).Append('7')
            .Append(Row(_height - 2)).Append(new string('-', width))
            .Append(Row(_height - 1)).Append(StatusLineFormatter.FormatStatus(state, width))
            .Append(Row(_height)).Append(StatusLineFormatter.FormatKeyHelp(width))
            .Append(Escape).Append('8');

        _terminal.Write(text.ToString());
    }

    private void RestoreScreen()
    {
        _terminal.Write($"{ResetScrollRegion()}{Escape}[{_height};1H\r\n{Escape}[?25h");
    }

    private static string ResetScrollRegion()
    {
        return $"{Escape}[r";
    }

    /// <summary>Cursor to the start of the row and the row cleared.</summary>
    private static string Row(int row)
    {
        return $"{Escape}[{Math.Max(1, row)};1H{Escape}[2K";
    }

    /// <summary>The process's own terminal; ANSI processing is switched on through the console mode on Windows.</summary>
    private sealed partial class SystemConsoleTerminal : IConsoleTerminal
    {
        private const int StandardOutputHandle = -11;
        private const uint EnableVirtualTerminalProcessing = 0x0004;

        public bool IsInputRedirected => Console.IsInputRedirected;

        public bool IsOutputRedirected => Console.IsOutputRedirected;

        public int Width => Console.WindowWidth;

        public int Height => Console.WindowHeight;

        public bool KeyAvailable => Console.KeyAvailable;

        public bool TryEnableAnsi()
        {
            if (!OperatingSystem.IsWindows())
            {
                // Every other supported terminal processes escape sequences as is.
                return true;
            }

            nint handle = GetStdHandle(StandardOutputHandle);

            return handle != nint.Zero && handle != -1 && GetConsoleMode(handle, out uint mode) &&
                ((mode & EnableVirtualTerminalProcessing) != 0 || SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing));
        }

        public void Write(string text)
        {
            Console.Out.Write(text);
        }

        public ConsoleKeyInfo ReadKey()
        {
            return Console.ReadKey(intercept: true);
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial nint GetStdHandle(int standardHandle);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetConsoleMode(nint handle, out uint mode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetConsoleMode(nint handle, uint mode);
    }
}
