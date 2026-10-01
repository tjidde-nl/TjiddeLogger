namespace Tjidde.Logging.Formatting;

/// <summary>
/// Defines the contract for formatting exceptions into a readable log string.
/// </summary>
public interface IExceptionFormatter
{
    /// <summary>
    /// Formats the given exception (and optionally its inner exceptions) into a structured string.
    /// </summary>
    string Format(Exception exception);
}
