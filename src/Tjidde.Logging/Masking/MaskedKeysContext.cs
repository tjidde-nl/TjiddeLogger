namespace Tjidde.Logging.Masking;

/// <summary>
/// Provides global access to a collection of extra sensitive keys that should be masked.
/// These keys are added dynamically at runtime from within the application and apply
/// immediately to all loggers, including loggers that already exist.
/// </summary>
public static class MaskedKeysContext
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);

    // Read-only copy of Keys, replaced on every change. Readers get the same instance until the
    // keys change, so maskers can detect changes with a cheap reference comparison.
    private static IReadOnlyCollection<string> _snapshot = Array.Empty<string>();

    /// <summary>
    /// Adds one or more keys to the collection of extra sensitive keywords for censoring.
    /// </summary>
    /// <param name="keys">The keys to add.</param>
    public static void Add(params string[] keys)
    {
        if (keys is null) return;

        lock (SyncRoot)
        {
            var changed = false;
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    changed |= Keys.Add(key);
            }

            if (changed)
                PublishSnapshot();
        }
    }

    /// <summary>
    /// Removes one or more keys from the collection of extra sensitive keywords.
    /// </summary>
    /// <param name="keys">The keys to remove.</param>
    public static void Remove(params string[] keys)
    {
        if (keys is null) return;

        lock (SyncRoot)
        {
            var changed = false;
            foreach (var key in keys)
            {
                if (key is not null)
                    changed |= Keys.Remove(key);
            }

            if (changed)
                PublishSnapshot();
        }
    }

    /// <summary>
    /// Clears all extra sensitive keys added via this context.
    /// </summary>
    public static void Clear()
    {
        lock (SyncRoot)
        {
            if (Keys.Count == 0) return;

            Keys.Clear();
            PublishSnapshot();
        }
    }

    /// <summary>
    /// Returns the current collection of extra sensitive keys. The collection is a read-only snapshot;
    /// the same instance is returned until the keys change.
    /// </summary>
    public static IReadOnlyCollection<string> GetKeys() => Volatile.Read(ref _snapshot);

    private static void PublishSnapshot() => Volatile.Write(ref _snapshot, Array.AsReadOnly(Keys.ToArray()));
}
