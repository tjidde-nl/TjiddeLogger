using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.DemoApp.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes to <see cref="InMemoryLogSink"/> so the demo UI
/// can display log entries alongside the Tjidde console output.
/// </summary>
[ProviderAlias("InMemory")]
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly InMemoryLogSink _sink;

    public InMemoryLoggerProvider(InMemoryLogSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName) =>
        new InMemoryLogger(categoryName, _sink);

    public void Dispose() { }
}

internal sealed class InMemoryLogger : ILogger
{
    private readonly string _category;
    private readonly InMemoryLogSink _sink;

    public InMemoryLogger(string category, InMemoryLogSink sink)
    {
        _category = category;
        _sink = sink;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var exceptionDetails = exception is not null
            ? FormatException(exception)
            : null;

        _sink.Add(new LogEntry(
            Timestamp: DateTimeOffset.Now,
            Level: logLevel,
            Category: _category,
            FormattedMessage: message,
            ExceptionDetails: exceptionDetails));
    }

    private static string FormatException(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var current = ex;
        var depth = 0;
        while (current is not null)
        {
            if (depth > 0) sb.AppendLine("  ---> Inner exception:");
            sb.AppendLine($"  {current.GetType().FullName}: {current.Message}");
            if (current.StackTrace is { } st)
                sb.AppendLine(st);
            current = current.InnerException;
            depth++;
        }
        return sb.ToString().TrimEnd();
    }
}
