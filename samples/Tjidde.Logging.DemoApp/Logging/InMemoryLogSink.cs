using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.DemoApp.Logging;

/// <summary>
/// A thread-safe in-memory sink that captures formatted log entries for display in the demo UI.
/// </summary>
public sealed class InMemoryLogSink
{
    private readonly List<LogEntry> _entries = new();
    private readonly Lock _lock = new();

    public event Action? OnChanged;

    public void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            // Keep a reasonable cap so the UI stays responsive
            if (_entries.Count > 200)
                _entries.RemoveAt(0);
        }
        OnChanged?.Invoke();
    }

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
            return _entries.ToList();
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
        OnChanged?.Invoke();
    }
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string FormattedMessage,
    string? ExceptionDetails);
