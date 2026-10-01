using Tjidde.Logging.Formatting;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.DesktopApp.Logging;

[ProviderAlias("InMemory")]
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly InMemoryLogSink _sink;

    public InMemoryLoggerProvider(InMemoryLogSink sink) => _sink = sink;

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, _sink);

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

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var exDetails = exception is not null ? new ExceptionFormatter().Format(exception) : null;

        _sink.Add(new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = logLevel,
            Category = _category,
            FormattedMessage = message,
            ExceptionDetails = exDetails
        });
    }
}
