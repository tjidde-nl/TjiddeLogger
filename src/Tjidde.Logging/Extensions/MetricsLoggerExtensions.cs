using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.Extensions;

/// <summary>
/// Extension methods for writing metric-style log entries (for example <c>requests_total=42</c>).
/// </summary>
/// <remarks>
/// Metrics entries are written at <see cref="LogLevel.Information"/> with the event
/// <see cref="MetricsEventId"/>. Tjidde.Logging recognizes that event and shows the entry as
/// <c>[METRICS]</c> in magenta; every other logging provider sees an ordinary information entry.
/// For real application metrics (counters, histograms, gauges) prefer <c>System.Diagnostics.Metrics</c>.
/// </remarks>
public static class MetricsLoggerExtensions
{
    /// <summary>The event name that marks a log entry as a metrics entry.</summary>
    public const string MetricsEventName = "Metrics";

    /// <summary>The fixed event id used for metrics entries.</summary>
    public const int MetricsEventIdValue = 10_000;

    /// <summary>
    /// The event ID that marks a log entry as a metrics entry (<c>Id = 10000</c>, <c>Name = "Metrics"</c>).
    /// Any entry whose event name is <c>"Metrics"</c> is shown as <c>[METRICS]</c> by Tjidde.Logging.
    /// </summary>
    public static readonly EventId MetricsEventId = new(MetricsEventIdValue, MetricsEventName);

    /// <summary>
    /// The former custom log level for metrics entries (integer value 10).
    /// </summary>
    /// <remarks>
    /// Other logging providers do not know this level; the Microsoft console formatters, for example,
    /// throw an <see cref="ArgumentOutOfRangeException"/> for it. Tjidde.Logging still recognizes it,
    /// but use the <c>LogMetrics</c> methods (which log at <see cref="LogLevel.Information"/> with
    /// <see cref="MetricsEventId"/>) instead, or <c>System.Diagnostics.Metrics</c> for real metrics.
    /// </remarks>
    [Obsolete("(LogLevel)10 is not a valid LogLevel and makes other logging providers (such as the Microsoft console logger) throw. " +
              "Use LogMetrics(...), which logs at LogLevel.Information with MetricsEventId, " +
              "or System.Diagnostics.Metrics for real application metrics.")]
    public static readonly LogLevel Metrics = (LogLevel)10;

    /// <summary>
    /// Logs a metrics entry at <see cref="LogLevel.Information"/> with <see cref="MetricsEventId"/>.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to write to.</param>
    /// <param name="message">The log message.</param>
    /// <param name="args">Optional message format arguments.</param>
    public static void LogMetrics(this ILogger logger, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.Log(LogLevel.Information, MetricsEventId, message, args);
    }

    /// <summary>
    /// Logs a metrics entry with an associated exception at <see cref="LogLevel.Information"/> with <see cref="MetricsEventId"/>.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to write to.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="message">The log message.</param>
    /// <param name="args">Optional message format arguments.</param>
    public static void LogMetrics(this ILogger logger, Exception? exception, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.Log(LogLevel.Information, MetricsEventId, exception, message, args);
    }

    /// <summary>
    /// Logs a metrics entry at <see cref="LogLevel.Information"/> with a caller-supplied event ID.
    /// </summary>
    /// <remarks>
    /// The event ID is passed on unchanged. When it has no name, the name <c>"Metrics"</c> is used so the
    /// entry is still shown as <c>[METRICS]</c>; an event ID with a different name is shown as <c>[INFORMATION]</c>.
    /// </remarks>
    /// <param name="logger">The <see cref="ILogger"/> to write to.</param>
    /// <param name="eventId">The event ID.</param>
    /// <param name="message">The log message.</param>
    /// <param name="args">Optional message format arguments.</param>
    public static void LogMetrics(this ILogger logger, EventId eventId, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var id = string.IsNullOrEmpty(eventId.Name) ? new EventId(eventId.Id, MetricsEventName) : eventId;
        logger.Log(LogLevel.Information, id, message, args);
    }

    /// <summary>Returns <c>true</c> when the entry is a metrics entry (event name "Metrics" or the obsolete level 10).</summary>
    internal static bool IsMetricsEntry(LogLevel logLevel, EventId eventId)
        => (int)logLevel == 10 || string.Equals(eventId.Name, MetricsEventName, StringComparison.Ordinal);
}
