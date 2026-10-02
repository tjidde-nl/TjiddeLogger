namespace Tjidde.Logging.Masking;

/// <summary>
/// Static, process-wide collection of extra sensitive keys that should be masked.
/// These keys are added dynamically at runtime from within the application and apply
/// immediately to all loggers that use the default <see cref="GlobalMaskedKeysAccessor"/>,
/// including loggers that already exist.
/// </summary>
/// <remarks>
/// This is the convenience variant: every host and test in the process shares these keys.
/// For isolated, testable code, register a <see cref="MaskedKeysStore"/> per host with
/// <c>UseIsolatedMaskedKeys()</c> and inject it instead.
/// </remarks>
public static class MaskedKeysContext
{
    private static readonly MaskedKeysStore Store = new();

    /// <summary>
    /// Adds one or more keys to the collection of extra sensitive keywords for censoring.
    /// </summary>
    /// <param name="keys">The keys to add.</param>
    public static void Add(params string[] keys) => Store.Add(keys);

    /// <summary>
    /// Removes one or more keys from the collection of extra sensitive keywords.
    /// </summary>
    /// <param name="keys">The keys to remove.</param>
    public static void Remove(params string[] keys) => Store.Remove(keys);

    /// <summary>
    /// Clears all extra sensitive keys added via this context.
    /// </summary>
    public static void Clear() => Store.Clear();

    /// <summary>
    /// Returns the current collection of extra sensitive keys. The collection is a read-only snapshot;
    /// the same instance is returned until the keys change.
    /// </summary>
    public static IReadOnlyCollection<string> GetKeys() => Store.GetKeys();
}
