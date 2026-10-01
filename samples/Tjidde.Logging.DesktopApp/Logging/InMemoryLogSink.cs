using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Tjidde.Logging.DesktopApp.Logging;

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
