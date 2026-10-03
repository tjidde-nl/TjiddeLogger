using Microsoft.Extensions.Logging;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Options;

namespace Tjidde.Logging.Sinks;

/// <summary>
/// One log entry as written by Tjidde.Logging, passed to every <see cref="ILogSink"/>. Immutable.
/// </summary>
/// <remarks>
/// Every text in the entry has gone through the same sensitive data masking as the console output
/// (when <see cref="TjiddeLoggerOptions.EnableSensitiveDataMasking"/> is on): the entry never contains
/// unmasked values.
/// </remarks>
public sealed class TjiddeLogEntry
{
    /// <summary>When the entry was logged: UTC or local time, depending on <see cref="TjiddeLoggerOptions.UseUtcTimestamp"/>.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The log level. Metrics entries have <see cref="LogLevel.Information"/>; see <see cref="IsMetrics"/>.</summary>
    public LogLevel Level { get; init; }

    /// <summary>The event ID of the entry: <see cref="MetricsLoggerExtensions.MetricsEventId"/> for <c>LogMetrics</c> entries.</summary>
    public EventId EventId { get; init; }

    /// <summary>The logger category, usually the full type name.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>The last segment of <see cref="Category"/>, as shown after <c>Class=&gt;</c>.</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>The method name from a <c>MethodName</c> scope (or the stack trace, when enabled); otherwise <see langword="null"/>.</summary>
    public string? MethodName { get; init; }

    /// <summary>The customer from the customer context, or <see langword="null"/>.</summary>
    public string? Customer { get; init; }

    /// <summary>The rendered, masked message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The formatted, masked exception (type, message, stack trace, inner exceptions), or <see langword="null"/>.</summary>
    public string? FormattedException { get; init; }

    /// <summary>The complete line as Tjidde.Logging renders it for the console: text or JSON, depending on <see cref="OutputFormat"/>.</summary>
    public string RenderedLine { get; init; } = string.Empty;

    /// <summary>The format of <see cref="RenderedLine"/>.</summary>
    public TjiddeLogOutputFormat OutputFormat { get; init; }

    /// <summary>
    /// Whether this is a metrics entry (written by <c>LogMetrics</c>: event name <c>"Metrics"</c>),
    /// shown as <c>[METRICS]</c> in the console.
    /// </summary>
    public bool IsMetrics => MetricsLoggerExtensions.IsMetricsEntry(Level, EventId);

    /// <summary>Returns <see cref="RenderedLine"/>.</summary>
    public override string ToString() => RenderedLine;
}
