namespace Tjidde.Logging.Context;

/// <summary>
/// Holds the ambient customer context for the current async execution flow.
/// Use <see cref="Set"/> to assign a customer identifier at the start of a request or operation,
/// and <see cref="Current"/> to read it anywhere in the call chain.
/// </summary>
public static class CustomerContext
{
    private static readonly AsyncLocal<string?> _current = new();

    /// <summary>
    /// Gets the current customer context value, or null if none is set.
    /// </summary>
    public static string? Current => _current.Value;

    /// <summary>
    /// Sets the customer context for the current async execution flow.
    /// </summary>
    /// <param name="customerIdentifier">The customer identifier or prefix to associate with log entries.</param>
    public static void Set(string? customerIdentifier)
    {
        _current.Value = customerIdentifier;
    }

    /// <summary>
    /// Clears the customer context for the current async execution flow.
    /// </summary>
    public static void Clear()
    {
        _current.Value = null;
    }
}
