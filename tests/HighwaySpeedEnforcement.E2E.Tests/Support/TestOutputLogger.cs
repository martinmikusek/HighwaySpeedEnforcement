using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>Writes log lines of the component under test into the output of the running test.</summary>
public sealed class TestOutputLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullLogger.Instance.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Debug;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff} [{logLevel}] {typeof(T).Name}: {formatter(state, exception)}";

        if (exception is not null)
        {
            line += $" ({exception.GetType().Name}: {exception.Message})";
        }

        TestContext.Current.TestOutputHelper?.WriteLine(line);
    }
}
