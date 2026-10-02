using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Formatting;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tjidde.Logging.Logging;

/// <summary>
/// Core logger implementation that writes structured, readable log entries
/// in the Tjidde standard format: YYYY-MM-DD: HH:mm:ss: ClassName MethodName: Customer: Message
/// </summary>
internal sealed class TjiddeLogger : ILogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Logged objects can reference each other; write null for a cycle instead of throwing.
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        // Very deep object graphs fall back to ToString() instead of producing huge lines.
        MaxDepth = 32
    };

    private readonly string _categoryName;
    private readonly TjiddeLoggerConfigurationHolder _configuration;
    private readonly ICustomerContextAccessor _customerContextAccessor;
    private readonly ConsoleLogProcessor _processor;
    private readonly TimeProvider _timeProvider;
    private IExternalScopeProvider? _scopeProvider;

    // Minimum level resolved for this category, together with the configuration it was resolved from.
    // Replaced (not mutated) when the configuration changes, so readers always see a matching pair.
    private MinimumLevelCache? _minimumLevel;

    // Extracted from category name for readable output
    private readonly string _className;

    /// <summary>
    /// Creates a logger that reads its settings from <paramref name="configuration"/> on every call,
    /// so a configuration change applies to this logger immediately.
    /// Timestamps come from <paramref name="timeProvider"/> (default: <see cref="TimeProvider.System"/>).
    /// </summary>
    public TjiddeLogger(
        string categoryName,
        TjiddeLoggerConfigurationHolder configuration,
        ICustomerContextAccessor customerContextAccessor,
        ConsoleLogProcessor processor,
        IExternalScopeProvider? scopeProvider,
        TimeProvider? timeProvider = null)
    {
        _categoryName = categoryName;
        _configuration = configuration;
        _customerContextAccessor = customerContextAccessor;
        _processor = processor;
        _scopeProvider = scopeProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _className = ExtractClassName(categoryName);
    }

    /// <summary>Creates a logger with a fixed configuration.</summary>
    public TjiddeLogger(
        string categoryName,
        TjiddeLoggerOptions options,
        ICustomerContextAccessor customerContextAccessor,
        ISensitiveDataMasker masker,
        IExceptionFormatter exceptionFormatter,
        ConsoleLogProcessor processor,
        IExternalScopeProvider? scopeProvider,
        TimeProvider? timeProvider = null)
        : this(
            categoryName,
            new TjiddeLoggerConfigurationHolder(new TjiddeLoggerConfiguration(options, masker, exceptionFormatter)),
            customerContextAccessor,
            processor,
            scopeProvider,
            timeProvider)
    {
    }

    /// <summary>The configuration this logger currently uses.</summary>
    internal TjiddeLoggerConfiguration Configuration => _configuration.Current;

    /// <summary>Updates the scope provider — called by the provider when the external scope provider is set.</summary>
    internal void SetScopeProvider(IExternalScopeProvider scopeProvider)
        => _scopeProvider = scopeProvider;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _scopeProvider?.Push(state);

    public bool IsEnabled(LogLevel logLevel) => IsEnabled(_configuration.Current, logLevel);

    private bool IsEnabled(TjiddeLoggerConfiguration config, LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
            return false;

        return logLevel >= GetMinimumLevel(config);
    }

    private LogLevel GetMinimumLevel(TjiddeLoggerConfiguration config)
    {
        var cached = Volatile.Read(ref _minimumLevel);
        if (cached is not null && ReferenceEquals(cached.Configuration, config))
            return cached.Level;

        // First call, or the configuration changed: resolve once and reuse until the next change.
        var level = config.ResolveMinimumLevel(_categoryName);
        Volatile.Write(ref _minimumLevel, new MinimumLevelCache(config, level));
        return level;
    }

    private sealed class MinimumLevelCache
    {
        public MinimumLevelCache(TjiddeLoggerConfiguration configuration, LogLevel level)
        {
            Configuration = configuration;
            Level = level;
        }

        public TjiddeLoggerConfiguration Configuration { get; }
        public LogLevel Level { get; }
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // One configuration for the whole call, so a concurrent options change never mixes old and new settings.
        var config = _configuration.Current;

        // Metrics entries (LogMetrics) are never dropped by Tjidde's own minimum level, as before.
        var isMetrics = MetricsLoggerExtensions.IsMetricsEntry(logLevel, eventId);
        if (logLevel == LogLevel.None || (!isMetrics && !IsEnabled(config, logLevel)))
            return;

        // Logging must never throw: any failure while building the entry produces a minimal line instead.
        var now = GetTimestamp(config);
        string? message = null;
        try
        {
            WriteEntry(config, now, logLevel, eventId, state, exception, formatter, ref message);
        }
        catch (Exception renderError)
        {
            WriteFallbackEntry(config, now, logLevel, eventId, state, exception, formatter, message, renderError);
        }
    }

    /// <summary>
    /// The timestamp for an entry: UTC when <see cref="TjiddeLoggerOptions.UseUtcTimestamp"/> is set, otherwise
    /// local time (the time provider's local time zone). Falls back to the system clock if the provider throws.
    /// </summary>
    private DateTimeOffset GetTimestamp(TjiddeLoggerConfiguration config)
    {
        var utc = config.Options.UseUtcTimestamp;
        try
        {
            return utc ? _timeProvider.GetUtcNow() : _timeProvider.GetLocalNow();
        }
        catch (Exception)
        {
            // Logging must never throw, not even when a custom TimeProvider does.
            return utc ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
        }
    }

    private void WriteEntry<TState>(
        TjiddeLoggerConfiguration config,
        DateTimeOffset now,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        ref string? message)
    {
        var customer = _customerContextAccessor.GetCustomerContext();
        var methodName = ResolveMethodName(config);

        var maskedKeyNames = new List<string>();
        List<KeyValuePair<string, object?>>? properties = null;
        string? originalFormat = null;

        // Collect structured properties once, masking sensitive values so they never
        // reach any output (text, JSON or OpenTelemetry) unmasked.
        if (state is IEnumerable<KeyValuePair<string, object?>> structuredState)
        {
            properties = new List<KeyValuePair<string, object?>>();
            foreach (var kvp in structuredState)
            {
                if (kvp.Key.Equals("{OriginalFormat}", StringComparison.Ordinal))
                {
                    originalFormat = kvp.Value as string;
                    continue;
                }

                properties.Add(new KeyValuePair<string, object?>(kvp.Key, SafeMaskPropertyValue(config, kvp.Key, kvp.Value, maskedKeyNames)));
            }
        }

        message = RenderMessage(config, state, exception, formatter, originalFormat, properties, maskedKeyNames.Count > 0);

        List<string>? scopes = null;
        if (config.Options.IncludeScopes && _scopeProvider is not null)
            scopes = CollectScopes(config);

        var rendered = config.Options.OutputFormat == TjiddeLogOutputFormat.Json
            ? BuildJsonLogLine(config, now, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames, properties)
            : BuildTextLogLine(config, now, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames);

        ExportToOpenTelemetry(config, now, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames, properties);

        _processor.Enqueue(rendered, config.Options.OutputFormat == TjiddeLogOutputFormat.Text ? GetColor(logLevel, eventId) : null);
    }

    /// <summary>
    /// Last-resort output when building the regular entry failed: level, category, message and a note
    /// that rendering failed. Never throws.
    /// </summary>
    private void WriteFallbackEntry<TState>(
        TjiddeLoggerConfiguration config,
        DateTimeOffset now,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        string? message,
        Exception renderError)
    {
        try
        {
            message ??= FallbackMessage(config, state, exception, formatter) ?? "[message unavailable]";
            var level = FormatLogLevel(logLevel, eventId);
            var failure = TryMask(config, $"{renderError.GetType().Name}: {SafeExceptionMessage(renderError)}")
                          ?? renderError.GetType().Name;

            string line;
            if (config.Options.OutputFormat == TjiddeLogOutputFormat.Json)
            {
                line = JsonSerializer.Serialize(new Dictionary<string, string?>
                {
                    ["@timestamp"] = now.ToString("O"),
                    ["message"] = message,
                    ["level"] = level,
                    ["category"] = _categoryName,
                    ["renderError"] = $"Log entry could not be rendered completely: {failure}"
                });
            }
            else
            {
                line = $"{now:yyyy-MM-dd}: {now:HH:mm:ss}: [{level}] Class=>{_className}: {message} " +
                       $"[Log entry could not be rendered completely: {failure}] | Category: {_categoryName}";
            }

            _processor.Enqueue(line, config.Options.OutputFormat == TjiddeLogOutputFormat.Text ? GetColor(logLevel, eventId) : null);
        }
        catch (Exception)
        {
            // Logging must never crash the application.
        }
    }

    /// <summary>
    /// The message for the last-resort entry when the regular message was never rendered. With masking enabled,
    /// structured state yields its unrendered template, because the framework's formatter inserts raw
    /// (unmasked) values of sensitive properties.
    /// </summary>
    private string? FallbackMessage<TState>(TjiddeLoggerConfiguration config, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (config.Options.EnableSensitiveDataMasking && state is IEnumerable<KeyValuePair<string, object?>> structuredState)
        {
            try
            {
                foreach (var kvp in structuredState)
                {
                    if (kvp.Key.Equals("{OriginalFormat}", StringComparison.Ordinal))
                        return TryMask(config, kvp.Value as string);
                }
            }
            catch (Exception)
            {
                // Fall through.
            }

            return null;
        }

        return TryMask(config, TryFormat(state, exception, formatter));
    }

    private static string? TryFormat<TState>(TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            return formatter(state, exception);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? TryMask(TjiddeLoggerConfiguration config, string? text)
    {
        if (text is null || !config.Options.EnableSensitiveDataMasking)
            return text;

        try
        {
            return config.Masker.MaskMessage(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SafeExceptionMessage(Exception exception)
    {
        try
        {
            return exception.Message;
        }
        catch (Exception)
        {
            return "[message unavailable]";
        }
    }

    /// <summary>Returns <c>value.ToString()</c>, or <c>[unserializable: TypeName]</c> when ToString throws.</summary>
    internal static string? SafeToString(object? value)
    {
        if (value is null)
            return null;

        try
        {
            return value.ToString();
        }
        catch (Exception)
        {
            return Unserializable(value);
        }
    }

    private static string Unserializable(object value) => $"[unserializable: {value.GetType().FullName}]";

    private string RenderMessage<TState>(
        TjiddeLoggerConfiguration config,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        string? originalFormat,
        List<KeyValuePair<string, object?>>? properties,
        bool hasMaskedProperties)
    {
        string message;
        try
        {
            if (!config.Options.EnableSensitiveDataMasking)
                return formatter(state, exception);

            // The framework's formatter inserts the raw values of sensitive properties into the message,
            // so rebuild the message from its template using the masked values instead.
            message = hasMaskedProperties
                      && originalFormat is not null
                      && properties is not null
                      && MessageTemplateRenderer.TryRender(originalFormat, properties, out var rendered)
                ? rendered
                : formatter(state, exception);
        }
        catch (Exception renderError)
        {
            // A value whose ToString() or enumeration throws must not lose the entry: fall back to the template.
            message = $"{originalFormat ?? "[message unavailable]"} [message could not be rendered: {renderError.GetType().Name}]";
        }

        return config.Options.EnableSensitiveDataMasking ? config.Masker.MaskMessage(message) : message;
    }

    private object? SafeMaskPropertyValue(TjiddeLoggerConfiguration config, string key, object? value, List<string> maskedKeyNames)
    {
        try
        {
            return MaskPropertyValue(config, key, value, maskedKeyNames);
        }
        catch (Exception)
        {
            // Only reachable for sensitive keys (ToString threw); never fall back to the raw value there.
            return value is null ? null : Unserializable(value);
        }
    }

    private object? MaskPropertyValue(TjiddeLoggerConfiguration config, string key, object? value, List<string> maskedKeyNames)
    {
        if (!config.Options.EnableSensitiveDataMasking)
            return value;

        if (config.Masker.IsSensitiveKey(key))
        {
            maskedKeyNames.Add(key);
            return config.Masker.MaskValue(key, value?.ToString() ?? string.Empty);
        }

        // Non-sensitive keys can still carry sensitive literals (e.g. "password=..." or dynamic keys).
        return value is string text ? config.Masker.MaskMessage(text) : value;
    }

    private string FormatException(TjiddeLoggerConfiguration config, Exception exception)
    {
        string formatted;
        try
        {
            formatted = config.ExceptionFormatter.Format(exception);
        }
        catch (Exception formatterError)
        {
            // A custom IExceptionFormatter must not make logging throw; fall back to type and message.
            formatted = $"{exception.GetType().FullName}: {SafeExceptionMessage(exception)} " +
                        $"[exception formatter failed: {formatterError.GetType().Name}]";
        }

        return config.Options.EnableSensitiveDataMasking ? config.Masker.MaskMessage(formatted) : formatted;
    }

    private void ExportToOpenTelemetry(
        TjiddeLoggerConfiguration config,
        DateTimeOffset timestamp,
        LogLevel logLevel,
        EventId eventId,
        string? methodName,
        string? customer,
        string message,
        Exception? exception,
        List<string>? scopes,
        List<string> maskedKeyNames,
        List<KeyValuePair<string, object?>>? properties)
    {
        if (!config.Options.EnableOpenTelemetryExport)
            return;

        try
        {
            var tags = BuildOpenTelemetryTags(config, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames, properties);
            var activityEvent = new ActivityEvent("log", timestamp, new ActivityTagsCollection(tags));

            var currentActivity = Activity.Current;
            if (currentActivity is not null)
            {
                currentActivity.AddEvent(activityEvent);
                return;
            }

            if (!config.Options.OpenTelemetryCreateFallbackActivity || config.FallbackActivitySource is null)
                return;

            using var fallbackActivity = config.FallbackActivitySource.StartActivity("tjidde.log", ActivityKind.Internal);
            fallbackActivity?.AddEvent(activityEvent);
        }
        catch (Exception)
        {
            // Exporting is best effort: a failing listener or tag value must not break logging.
        }
    }

    private IEnumerable<KeyValuePair<string, object?>> BuildOpenTelemetryTags(
        TjiddeLoggerConfiguration config,
        LogLevel logLevel,
        EventId eventId,
        string? methodName,
        string? customer,
        string message,
        Exception? exception,
        List<string>? scopes,
        List<string> maskedKeyNames,
        List<KeyValuePair<string, object?>>? properties)
    {
        yield return new KeyValuePair<string, object?>("log.category", _categoryName);
        yield return new KeyValuePair<string, object?>("log.class", _className);
        yield return new KeyValuePair<string, object?>("log.level", FormatLogLevel(logLevel, eventId));
        yield return new KeyValuePair<string, object?>("log.message", message);

        if (eventId.Id != 0)
            yield return new KeyValuePair<string, object?>("event.id", eventId.Id);

        if (!string.IsNullOrWhiteSpace(eventId.Name))
            yield return new KeyValuePair<string, object?>("event.name", eventId.Name);

        if (!string.IsNullOrWhiteSpace(customer))
            yield return new KeyValuePair<string, object?>("enduser.id", customer);

        if (!string.IsNullOrWhiteSpace(methodName))
            yield return new KeyValuePair<string, object?>("code.function", methodName);

        if (exception is not null)
        {
            yield return new KeyValuePair<string, object?>("exception.type", exception.GetType().FullName);
            var exceptionMessage = SafeExceptionMessage(exception);
            yield return new KeyValuePair<string, object?>("exception.message", config.Options.EnableSensitiveDataMasking ? config.Masker.MaskMessage(exceptionMessage) : exceptionMessage);
            yield return new KeyValuePair<string, object?>("exception.stacktrace", FormatException(config, exception));
        }

        if (scopes is { Count: > 0 })
            yield return new KeyValuePair<string, object?>("log.scopes", string.Join(" > ", scopes));

        if (maskedKeyNames.Count > 0)
            yield return new KeyValuePair<string, object?>("log.masked_fields", string.Join(",", maskedKeyNames));

        if (properties is not null)
        {
            foreach (var kvp in properties)
                yield return new KeyValuePair<string, object?>($"log.property.{kvp.Key}", kvp.Value);
        }
    }

    private string BuildTextLogLine(
        TjiddeLoggerConfiguration config,
        DateTimeOffset timestamp,
        LogLevel logLevel,
        EventId eventId,
        string? methodName,
        string? customer,
        string message,
        Exception? exception,
        List<string>? scopes,
        List<string> maskedKeyNames)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(BuildLogLine(timestamp, FormatLogLevel(logLevel, eventId), _className, methodName, customer, message));

        if (maskedKeyNames.Count > 0)
            sb.Append($" [Masked: {string.Join(", ", maskedKeyNames)}]");

        if (exception is not null)
            sb.Append($" | Exception: {FormatException(config, exception)}");

        if (scopes is { Count: > 0 })
            sb.Append($" | Scopes: {string.Join(" > ", scopes)}");

        return sb.ToString();
    }

    private string BuildJsonLogLine(
        TjiddeLoggerConfiguration config,
        DateTimeOffset timestamp,
        LogLevel logLevel,
        EventId eventId,
        string? methodName,
        string? customer,
        string message,
        Exception? exception,
        List<string>? scopes,
        List<string> maskedKeyNames,
        List<KeyValuePair<string, object?>>? properties)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@timestamp"] = timestamp.ToString("O"),
            ["message"] = message,
            ["level"] = FormatLogLevel(logLevel, eventId),
            ["category"] = _categoryName,
            ["class"] = _className,
            ["eventId"] = eventId.Id,
            ["eventName"] = eventId.Name,
            ["customer"] = customer,
            ["method"] = methodName,
            ["scopes"] = scopes is { Count: > 0 } ? scopes : null,
            ["maskedFields"] = maskedKeyNames.Count > 0 ? maskedKeyNames : null,
            ["exception"] = exception is null ? null : FormatException(config, exception)
        };

        if (properties is { Count: > 0 })
        {
            var propertyMap = new Dictionary<string, object?>();
            foreach (var kvp in properties)
                propertyMap[kvp.Key] = ToSerializableValue(config, kvp.Value);
            payload["properties"] = propertyMap;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Serializes one property value up front so a value that cannot be serialized (unsupported type,
    /// throwing getter, too deep) only affects itself: it falls back to <c>ToString()</c>, or to
    /// <c>[unserializable: TypeName]</c> when that throws too.
    /// </summary>
    private object? ToSerializableValue(TjiddeLoggerConfiguration config, object? value)
    {
        if (value is null)
            return null;

        try
        {
            return JsonSerializer.SerializeToElement(value, value.GetType(), JsonOptions);
        }
        catch (Exception)
        {
            var text = SafeToString(value);
            return config.Options.EnableSensitiveDataMasking && text is not null ? config.Masker.MaskMessage(text) : text;
        }
    }

    private static string BuildLogLine(
        DateTimeOffset timestamp,
        string level,
        string className,
        string? methodName,
        string? customer,
        string message)
    {
        var date = timestamp.ToString("yyyy-MM-dd");
        var time = timestamp.ToString("HH:mm:ss");
        var method = string.IsNullOrWhiteSpace(methodName) ? string.Empty : $" Method=>{methodName}";
        var customerPart = string.IsNullOrWhiteSpace(customer) ? string.Empty : $"Client=>{customer}: ";

        return $"{date}: {time}: [{level}] Class=>{className}{method}: {customerPart}{message}";
    }

    private static string FormatLogLevel(LogLevel logLevel, EventId eventId) => logLevel switch
    {
        _ when MetricsLoggerExtensions.IsMetricsEntry(logLevel, eventId) => "METRICS",
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFORMATION",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => "???"
    };

    private string? ResolveMethodName(TjiddeLoggerConfiguration config)
        => ExtractMethodFromScope(config)
           ?? (config.Options.ResolveMethodNameFromStackTrace ? ExtractMethodFromStack() : null);

    private string? ExtractMethodFromScope(TjiddeLoggerConfiguration config)
    {
        if (!config.Options.IncludeScopes || _scopeProvider is null)
            return null;

        string? methodName = null;
        _scopeProvider.ForEachScope((scope, _) =>
        {
            if (scope is not IEnumerable<KeyValuePair<string, object?>> kvps) return;
            foreach (var kvp in kvps)
            {
                if (kvp.Key.Equals("MethodName", StringComparison.OrdinalIgnoreCase) && kvp.Value is string m)
                    methodName = m;
            }
        }, (object?)null);

        return methodName;
    }

    private static string? ExtractMethodFromStack()
    {
        var stack = new StackTrace(skipFrames: 1, fNeedFileInfo: false);
        var frames = stack.GetFrames();
        if (frames is null)
            return null;

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method is null)
                continue;

            var methodName = method.Name;
            if (string.IsNullOrWhiteSpace(methodName) || methodName.StartsWith('<'))
                continue;

            var declaringType = method.DeclaringType;
            if (declaringType == typeof(TjiddeLogger))
                continue;

            var ns = declaringType?.Namespace;
            if (ns is not null && ns.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal))
                continue;

            return methodName;
        }

        return null;
    }

    private string FormatScopeProperty(TjiddeLoggerConfiguration config, string key, object? value)
    {
        var raw = SafeToString(value) ?? string.Empty;
        if (!config.Options.EnableSensitiveDataMasking)
            return $"{key}={raw}";

        var text = config.Masker.IsSensitiveKey(key)
            ? config.Masker.MaskValue(key, raw)
            : config.Masker.MaskMessage(raw);
        return $"{key}={text}";
    }

    private List<string> CollectScopes(TjiddeLoggerConfiguration config)
    {
        var scopes = new List<string>();
        _scopeProvider?.ForEachScope((scope, list) =>
        {
            try
            {
                switch (scope)
                {
                    case string s when !string.IsNullOrWhiteSpace(s):
                        list.Add(config.Options.EnableSensitiveDataMasking ? config.Masker.MaskMessage(s) : s);
                        break;
                    case IEnumerable<KeyValuePair<string, object?>> kvps:
                    {
                        list.AddRange(from kvp in kvps where !kvp.Key.Equals("MethodName", StringComparison.OrdinalIgnoreCase) select FormatScopeProperty(config, kvp.Key, kvp.Value));

                        break;
                    }
                }
            }
            catch (Exception)
            {
                // A scope whose enumeration throws is skipped instead of failing the whole entry.
                list.Add($"[unserializable scope: {scope?.GetType().FullName}]");
            }
        }, scopes);
        return scopes;
    }

    private static ConsoleColor GetColor(LogLevel logLevel, EventId eventId) => logLevel switch
    {
        _ when MetricsLoggerExtensions.IsMetricsEntry(logLevel, eventId) => ConsoleColor.Magenta,
        LogLevel.Trace => ConsoleColor.Gray,
        LogLevel.Debug => ConsoleColor.Cyan,
        LogLevel.Information => ConsoleColor.Green,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Critical => ConsoleColor.DarkRed,
        _ => ConsoleColor.White
    };

    private static string ExtractClassName(string categoryName)
    {
        // Category names are typically fully qualified type names; take the last segment
        var lastDot = categoryName.LastIndexOf('.');
        return lastDot >= 0 ? categoryName[(lastDot + 1)..] : categoryName;
    }
}
