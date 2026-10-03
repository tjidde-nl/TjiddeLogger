namespace Tjidde.Logging.Sinks;

/// <summary>
/// A thread-safe <see cref="ILogSink"/> that keeps the most recent entries in memory, for example to show them in a UI
/// or to assert on them in tests. When <see cref="Capacity"/> is reached, the oldest entry is dropped.
/// </summary>
/// <remarks>
/// Register it with <c>builder.AddTjiddeInMemorySink()</c> and inject <see cref="InMemoryLogSink"/> to read the entries.
/// </remarks>
public sealed class InMemoryLogSink : ILogSink
{
    /// <summary>The capacity used by the parameterless constructor.</summary>
    public const int DefaultCapacity = 1000;

    private readonly Queue<TjiddeLogEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>Creates a sink that keeps at most <see cref="DefaultCapacity"/> entries.</summary>
    public InMemoryLogSink()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Creates a sink that keeps at most <paramref name="capacity"/> entries.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public InMemoryLogSink(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The capacity must be at least 1.");

        Capacity = capacity;
    }

    /// <summary>
    /// Raised after an entry was added, on the thread that logged it. Keep handlers short and marshal to the UI
    /// thread yourself. An exception from a handler does not reach the code that logged.
    /// </summary>
    public event EventHandler<TjiddeLogEntry>? EntryAdded;

    /// <summary>Raised after <see cref="Clear"/> removed all entries, on the thread that called it.</summary>
    public event EventHandler? Cleared;

    /// <summary>The maximum number of entries kept.</summary>
    public int Capacity { get; }

    /// <summary>The number of entries currently kept.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    /// <inheritdoc />
    public void Write(TjiddeLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }

        EntryAdded?.Invoke(this, entry);
    }

    /// <summary>Returns a copy of the kept entries, oldest first. Later writes do not change the returned list.</summary>
    public IReadOnlyList<TjiddeLogEntry> GetSnapshot()
    {
        lock (_lock)
            return _entries.ToArray();
    }

    /// <summary>Removes all kept entries and raises <see cref="Cleared"/>.</summary>
    public void Clear()
    {
        lock (_lock)
            _entries.Clear();

        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
