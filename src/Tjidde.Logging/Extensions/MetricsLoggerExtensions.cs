using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.Extensions;

/// <summary>
/// Provides the custom <c>Metrics</c> log level and extension methods for logging metrics entries.
/// </summary>
public static class MetricsLoggerExtensions
{
    /// <summary>
    /// The custom log level used for metrics entries.
    /// Uses integer value 10 to avoid conflicts with all standard <see cref="LogLevel"/> values.
    /// </summary>
    public static readonly LogLevel Metrics = (LogLevel)10;

    /// <summary>
    /// Logs a metrics entry.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to write to.</param>
    /// <param name="message">The log message.</param>
    /// <param name="args">Optional message format arguments.</param>
    public static void LogMetrics(this ILogger logger, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.Log(Metrics, message, args);
    }

    /// <summary>
    /// Logs a metrics entry with an associated exception.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to write to.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="message">The log message.</param>
    /// <param name="args">Optional message format arguments.</param>
    public static void LogMetrics(this ILogger logger, Exception? exception, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.Log(Metrics, exception, message, args);
    }

    /// <summary>
    /// Logs a metrics entry with an event ID.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to write to.</param>
    /// <param name="eventId">The event ID.</param>
    /// <param name="message">The log message.</param>
    /// <param name="args">Optional message format arguments.</param>
    public static void LogMetrics(this ILogger logger, EventId eventId, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.Log(Metrics, eventId, message, args);
    }
}
