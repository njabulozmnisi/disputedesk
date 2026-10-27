using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>Keeps every log line, and every structured value logged with it, as text.</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new Logger(_lines);

    public void Dispose()
    {
        // Nothing to release: the lines stay readable after the host stops.
    }

    private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Enqueue(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach ((string key, object? value) in values)
                {
                    lines.Enqueue($"{key}={value}");
                }
            }
        }
    }
}
