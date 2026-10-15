using System.Text;
using HighwaySpeedEnforcement.Generator.Interactive;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

/// <summary>A terminal that records what was written and serves queued keys; an interactive 200x40 one by default (wide enough for the whole status line).</summary>
internal sealed class FakeTerminal : IConsoleTerminal
{
    private readonly Lock _lock = new();
    private readonly StringBuilder _written = new();
    private readonly Queue<ConsoleKeyInfo> _keys = new();

    public bool IsInputRedirected { get; init; }

    public bool IsOutputRedirected { get; init; }

    public int Width { get; set; } = 200;

    public int Height { get; set; } = 40;

    public bool AnsiSupported { get; init; } = true;

    public int EnableAnsiCalls { get; private set; }

    public string Written
    {
        get
        {
            lock (_lock)
            {
                return _written.ToString();
            }
        }
    }

    public bool KeyAvailable
    {
        get
        {
            lock (_lock)
            {
                return _keys.Count > 0;
            }
        }
    }

    public bool TryEnableAnsi()
    {
        EnableAnsiCalls++;
        return AnsiSupported;
    }

    public void Write(string text)
    {
        lock (_lock)
        {
            _written.Append(text);
        }
    }

    public ConsoleKeyInfo ReadKey()
    {
        lock (_lock)
        {
            return _keys.Dequeue();
        }
    }

    public void Press(char keyChar, ConsoleKey key = ConsoleKey.NoName)
    {
        lock (_lock)
        {
            _keys.Enqueue(new ConsoleKeyInfo(keyChar, key, shift: false, alt: false, control: false));
        }
    }

    public void ClearWritten()
    {
        lock (_lock)
        {
            _written.Clear();
        }
    }
}
