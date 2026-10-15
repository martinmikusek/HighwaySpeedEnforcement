namespace HighwaySpeedEnforcement.Generator.Interactive;

/// <summary>
/// The few things <see cref="InteractiveConsole"/> needs from a terminal, so that tests run against a fake and the real
/// <c>System.Console</c> is touched in one place only.
/// </summary>
public interface IConsoleTerminal
{
    bool IsInputRedirected { get; }

    bool IsOutputRedirected { get; }

    int Width { get; }

    int Height { get; }

    /// <summary>Turns on the processing of ANSI escape sequences; false when the terminal cannot do it.</summary>
    bool TryEnableAnsi();

    /// <summary>Writes the text as is (escape sequences included) to standard output.</summary>
    void Write(string text);

    bool KeyAvailable { get; }

    /// <summary>Reads one key without echoing it.</summary>
    ConsoleKeyInfo ReadKey();
}
