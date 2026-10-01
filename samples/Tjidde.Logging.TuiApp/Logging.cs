using Tjidde.Logging.Formatting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Tjidde.Logging.TuiApp.Logging;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Category { get; init; } = string.Empty;
    public string FormattedMessage { get; init; } = string.Empty;
    public string? ExceptionDetails { get; init; }
}

public sealed class InMemoryLogSink
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public event Action? OnChanged;

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        OnChanged?.Invoke();
    }

    public IReadOnlyList<LogEntry> GetEntries() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        OnChanged?.Invoke();
    }
}

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
