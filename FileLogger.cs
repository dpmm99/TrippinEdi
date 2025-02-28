using Microsoft.Extensions.Logging;

namespace TrippinEdi;

internal class FileLogger(string filePath) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        // No need to implement scope handling for this simple logger
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Enable all log levels
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var logRecord = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{formatter(state, exception)}";
        File.AppendAllText(filePath, logRecord + Environment.NewLine);
    }
}
