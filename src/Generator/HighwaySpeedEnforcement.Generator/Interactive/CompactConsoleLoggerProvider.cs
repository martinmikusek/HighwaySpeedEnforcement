using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HighwaySpeedEnforcement.Generator.Interactive;

/// <summary>
/// The one-line log format of the interactive mode (<c>HH:mm:ss level message</c>, design section 11), written through the
/// console's lock so that log lines and the status bar never interleave. Replaces the JSON console provider only when the
/// interactive console is active.
/// </summary>
public sealed class CompactConsoleLoggerProvider : ILoggerProvider
{
    private readonly InteractiveConsole _console;
    private readonly TimeProvider _timeProvider;

    public CompactConsoleLoggerProvider(InteractiveConsole console, TimeProvider timeProvider)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new CompactLogger(this);
    }

    public void Dispose()
    {
        // Nothing to release; the console is owned by the host.
    }

    public static string LevelLabel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }

    private sealed class CompactLogger : ILogger
    {
        private readonly CompactConsoleLoggerProvider _provider;

        public CompactLogger(CompactConsoleLoggerProvider provider)
        {
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = new StringBuilder()
                .Append(_provider._timeProvider.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                .Append(' ').Append(LevelLabel(logLevel))
                .Append(' ').Append(formatter(state, exception));

            if (exception is not null)
            {
                line.Append(Environment.NewLine).Append(exception);
            }

            _provider._console.WriteLine(line.ToString());
        }
    }
}
