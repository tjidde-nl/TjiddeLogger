namespace Tjidde.Logging.Options;

using Microsoft.Extensions.Logging;

/// <summary>
/// Configuration options for the Tjidde logger.
/// </summary>
public sealed class TjiddeLoggerOptions
{
    /// <summary>
    /// Output format used when writing log entries.
    /// Default: Text.
    /// </summary>
    public TjiddeLogOutputFormat OutputFormat { get; set; } = TjiddeLogOutputFormat.Text;

    internal const string MinimumLevelObsoleteMessage =
        "Use the standard Microsoft.Extensions.Logging filters instead: Logging:Tjidde:LogLevel in appsettings.json " +
        "(or AddFilter<TjiddeLoggerProvider>(...) in code). This option will be removed in 2.0.";

    /// <summary>
    /// Global minimum log level if no category-specific override is matched.
    /// Default: Trace.
    /// </summary>
    /// <remarks>
    /// Obsolete: use <c>Logging:Tjidde:LogLevel</c> in <c>appsettings.json</c> (or
    /// <c>AddFilter&lt;TjiddeLoggerProvider&gt;</c>) instead. This filter runs after the
    /// Microsoft.Extensions.Logging filters, so the stricter of the two wins. It will be removed in 2.0.
    /// </remarks>
    [Obsolete(MinimumLevelObsoleteMessage)]
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>
    /// Optional minimum levels per category/namespace/class, similar to appsettings Logging:LogLevel.
    /// Keys can be fully-qualified categories (for example My.App.Service), namespace prefixes
    /// (for example My.App), or Default for fallback behavior.
    /// </summary>
    /// <remarks>
    /// Obsolete: use <c>Logging:Tjidde:LogLevel</c> in <c>appsettings.json</c> (or
    /// <c>AddFilter&lt;TjiddeLoggerProvider&gt;</c>) instead. This filter runs after the
    /// Microsoft.Extensions.Logging filters, so the stricter of the two wins. It will be removed in 2.0.
    /// </remarks>
    [Obsolete(MinimumLevelObsoleteMessage)]
    public IDictionary<string, LogLevel> CategoryMinimumLevels { get; set; } =
        new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether timestamps are written in UTC instead of local time.
    /// Text output shows the time without an offset (<c>yyyy-MM-dd: HH:mm:ss</c>); JSON output writes
    /// <c>@timestamp</c> as ISO-8601 with the offset (<c>+00:00</c> in UTC, the local offset otherwise).
    /// The clock is the <see cref="System.TimeProvider"/> registered in DI, or <see cref="System.TimeProvider.System"/>.
    /// Default: false (local time).
    /// </summary>
    public bool UseUtcTimestamp { get; set; }

    /// <summary>
    /// Whether to emit each log entry as an OpenTelemetry event on the current <see cref="System.Diagnostics.Activity"/>.
    /// Default: false.
    /// </summary>
    public bool EnableOpenTelemetryExport { get; set; }

    /// <summary>
    /// Optional activity source name used when creating a short-lived fallback activity
    /// for OpenTelemetry export if no current activity exists.
    /// Default: Tjidde.Logging.
    /// </summary>
    public string OpenTelemetryActivitySourceName { get; set; } = "Tjidde.Logging";

    /// <summary>
    /// Whether to create a fallback activity for OpenTelemetry export when no current activity exists.
    /// Default: false.
    /// </summary>
    public bool OpenTelemetryCreateFallbackActivity { get; set; }

    /// <summary>
    /// Whether to include scope information in log output.
    /// Default: true.
    /// </summary>
    public bool IncludeScopes { get; set; } = true;

    /// <summary>
    /// Whether to resolve the calling method name from the stack trace when no <c>MethodName</c> scope is active.
    /// Walking the stack on every log call is expensive; prefer
    /// <see cref="Extensions.MethodScopeLoggerExtensions.BeginMethodScope"/>, which has no runtime cost.
    /// Default: false.
    /// </summary>
    public bool ResolveMethodNameFromStackTrace { get; set; }

    /// <summary>
    /// Whether to include the stack trace when logging exceptions.
    /// Default: true.
    /// </summary>
    public bool IncludeStackTrace { get; set; } = true;

    /// <summary>
    /// Whether to include inner exceptions in the formatted exception output.
    /// Default: true.
    /// </summary>
    public bool IncludeInnerExceptions { get; set; } = true;

    /// <summary>
    /// Whether to apply sensitive data masking to log messages and structured state.
    /// Default: true.
    /// </summary>
    public bool EnableSensitiveDataMasking { get; set; } = true;

    /// <summary>
    /// The placeholder used to replace masked sensitive values.
    /// Default: [REDACTED].
    /// </summary>
    public string MaskPlaceholder { get; set; } = "[REDACTED]";

    /// <summary>
    /// Additional field names or keywords to treat as sensitive, beyond the built-in defaults.
    /// These are matched case-insensitively against structured log property names and message fragments.
    /// </summary>
    public IList<string> AdditionalSensitiveKeys { get; set; } = new List<string>();
}
