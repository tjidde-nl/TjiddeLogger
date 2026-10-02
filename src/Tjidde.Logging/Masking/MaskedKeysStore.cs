namespace Tjidde.Logging.Masking;

/// <summary>
/// A thread-safe, non-static store of extra sensitive keys. Register one per host (see
/// <c>UseIsolatedMaskedKeys()</c>) and inject it where keys become known at runtime: the keys apply
/// immediately to every logger of that host, including loggers that already exist, and never leak
/// into other hosts or tests in the same process.
/// </summary>
/// <remarks>
/// <see cref="MaskedKeysContext"/> is the static, process-wide variant of this store.
/// </remarks>
public sealed class MaskedKeysStore : IMaskedKeysAccessor
{
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    // Read-only copy of _keys, replaced on every change. Readers get the same instance until the
    // keys change, so maskers can detect changes with a cheap reference comparison.
    private IReadOnlyCollection<string> _snapshot = Array.Empty<string>();

    /// <summary>
    /// Adds one or more keys. Blank keys are ignored.
    /// </summary>
    /// <param name="keys">The keys to add.</param>
    public void Add(params string[] keys)
    {
        if (keys is null) return;

        lock (_syncRoot)
        {
            var changed = false;
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    changed |= _keys.Add(key);
            }

            if (changed)
                PublishSnapshot();
        }
    }

    /// <summary>
    /// Removes one or more keys.
    /// </summary>
    /// <param name="keys">The keys to remove.</param>
    public void Remove(params string[] keys)
    {
        if (keys is null) return;

        lock (_syncRoot)
        {
            var changed = false;
            foreach (var key in keys)
            {
                if (key is not null)
                    changed |= _keys.Remove(key);
            }

            if (changed)
                PublishSnapshot();
        }
    }

    /// <summary>
    /// Removes all keys.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            if (_keys.Count == 0) return;

            _keys.Clear();
            PublishSnapshot();
        }
    }

    /// <summary>
    /// Returns the current keys as an immutable snapshot; the same instance is returned until the keys change.
    /// </summary>
    public IReadOnlyCollection<string> GetKeys() => Volatile.Read(ref _snapshot);

    private void PublishSnapshot() => Volatile.Write(ref _snapshot, Array.AsReadOnly(_keys.ToArray()));
}
