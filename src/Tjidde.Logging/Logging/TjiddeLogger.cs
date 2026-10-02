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
    private readonly ActivitySource? _fallbackActivitySource;
    private readonly TjiddeLoggerOptions _options;
    private readonly ICustomerContextAccessor _customerContextAccessor;
    private readonly ISensitiveDataMasker _masker;
    private readonly IExceptionFormatter _exceptionFormatter;
    private readonly ConsoleLogProcessor _processor;
    private IExternalScopeProvider? _scopeProvider;

    // Extracted from category name for readable output
    private readonly string _className;

    public TjiddeLogger(
        string categoryName,
        TjiddeLoggerOptions options,
        ICustomerContextAccessor customerContextAccessor,
        ISensitiveDataMasker masker,
        IExceptionFormatter exceptionFormatter,
        ConsoleLogProcessor processor,
        IExternalScopeProvider? scopeProvider)
    {
        _categoryName = categoryName;
        _options = options;
        _customerContextAccessor = customerContextAccessor;
        _masker = masker;
        _exceptionFormatter = exceptionFormatter;
        _processor = processor;
        _scopeProvider = scopeProvider;
        _className = ExtractClassName(categoryName);
        _fallbackActivitySource = string.IsNullOrWhiteSpace(_options.OpenTelemetryActivitySourceName)
            ? null
            : new ActivitySource(_options.OpenTelemetryActivitySourceName);
    }

    /// <summary>Updates the scope provider — called by the provider when the external scope provider is set.</summary>
    internal void SetScopeProvider(IExternalScopeProvider scopeProvider)
        => _scopeProvider = scopeProvider;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _scopeProvider?.Push(state);

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
            return false;

        var minimumLevel = ResolveMinimumLevelForCategory();
        return logLevel >= minimumLevel;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Metrics entries (LogMetrics) are never dropped by Tjidde's own minimum level, as before.
        var isMetrics = MetricsLoggerExtensions.IsMetricsEntry(logLevel, eventId);
        if (logLevel == LogLevel.None || (!isMetrics && !IsEnabled(logLevel)))
            return;

        // Logging must never throw: any failure while building the entry produces a minimal line instead.
        var now = DateTime.Now;
        string? message = null;
        try
        {
            WriteEntry(now, logLevel, eventId, state, exception, formatter, ref message);
        }
        catch (Exception renderError)
        {
            WriteFallbackEntry(now, logLevel, eventId, state, exception, formatter, message, renderError);
        }
    }

    private void WriteEntry<TState>(
        DateTime now,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        ref string? message)
    {
        var customer = _customerContextAccessor.GetCustomerContext();
        var methodName = ResolveMethodName();

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

                properties.Add(new KeyValuePair<string, object?>(kvp.Key, SafeMaskPropertyValue(kvp.Key, kvp.Value, maskedKeyNames)));
            }
        }

        message = RenderMessage(state, exception, formatter, originalFormat, properties, maskedKeyNames.Count > 0);

        List<string>? scopes = null;
        if (_options.IncludeScopes && _scopeProvider is not null)
            scopes = CollectScopes();

        var rendered = _options.OutputFormat == TjiddeLogOutputFormat.Json
            ? BuildJsonLogLine(now, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames, properties)
            : BuildTextLogLine(now, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames);

        ExportToOpenTelemetry(now, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames, properties);

        _processor.Enqueue(rendered, _options.OutputFormat == TjiddeLogOutputFormat.Text ? GetColor(logLevel, eventId) : null);
    }

    /// <summary>
    /// Last-resort output when building the regular entry failed: level, category, message and a note
    /// that rendering failed. Never throws.
    /// </summary>
    private void WriteFallbackEntry<TState>(
        DateTime now,
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
            message ??= FallbackMessage(state, exception, formatter) ?? "[message unavailable]";
            var level = FormatLogLevel(logLevel, eventId);
            var failure = TryMask($"{renderError.GetType().Name}: {SafeExceptionMessage(renderError)}")
                          ?? renderError.GetType().Name;

            string line;
            if (_options.OutputFormat == TjiddeLogOutputFormat.Json)
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

            _processor.Enqueue(line, _options.OutputFormat == TjiddeLogOutputFormat.Text ? GetColor(logLevel, eventId) : null);
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
    private string? FallbackMessage<TState>(TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (_options.EnableSensitiveDataMasking && state is IEnumerable<KeyValuePair<string, object?>> structuredState)
        {
            try
            {
                foreach (var kvp in structuredState)
                {
                    if (kvp.Key.Equals("{OriginalFormat}", StringComparison.Ordinal))
                        return TryMask(kvp.Value as string);
                }
            }
            catch (Exception)
            {
                // Fall through.
            }

            return null;
        }

        return TryMask(TryFormat(state, exception, formatter));
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

    private string? TryMask(string? text)
    {
        if (text is null || !_options.EnableSensitiveDataMasking)
            return text;

        try
        {
            return _masker.MaskMessage(text);
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
            if (!_options.EnableSensitiveDataMasking)
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

        return _options.EnableSensitiveDataMasking ? _masker.MaskMessage(message) : message;
    }

    private object? SafeMaskPropertyValue(string key, object? value, List<string> maskedKeyNames)
    {
        try
        {
            return MaskPropertyValue(key, value, maskedKeyNames);
        }
        catch (Exception)
        {
            // Only reachable for sensitive keys (ToString threw); never fall back to the raw value there.
            return value is null ? null : Unserializable(value);
        }
    }

    private object? MaskPropertyValue(string key, object? value, List<string> maskedKeyNames)
    {
        if (!_options.EnableSensitiveDataMasking)
            return value;

        if (_masker.IsSensitiveKey(key))
        {
            maskedKeyNames.Add(key);
            return _masker.MaskValue(key, value?.ToString() ?? string.Empty);
        }

        // Non-sensitive keys can still carry sensitive literals (e.g. "password=..." or dynamic keys).
        return value is string text ? _masker.MaskMessage(text) : value;
    }

    private string FormatException(Exception exception)
    {
        string formatted;
        try
        {
            formatted = _exceptionFormatter.Format(exception);
        }
        catch (Exception formatterError)
        {
            // A custom IExceptionFormatter must not make logging throw; fall back to type and message.
            formatted = $"{exception.GetType().FullName}: {SafeExceptionMessage(exception)} " +
                        $"[exception formatter failed: {formatterError.GetType().Name}]";
        }

        return _options.EnableSensitiveDataMasking ? _masker.MaskMessage(formatted) : formatted;
    }

    private void ExportToOpenTelemetry(
        DateTime timestamp,
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
        if (!_options.EnableOpenTelemetryExport)
            return;

        try
        {
            var tags = BuildOpenTelemetryTags(logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames, properties);
            var activityEvent = new ActivityEvent("log", timestamp, new ActivityTagsCollection(tags));

            var currentActivity = Activity.Current;
            if (currentActivity is not null)
            {
                currentActivity.AddEvent(activityEvent);
                return;
            }

            if (!_options.OpenTelemetryCreateFallbackActivity || _fallbackActivitySource is null)
                return;

            using var fallbackActivity = _fallbackActivitySource.StartActivity("tjidde.log", ActivityKind.Internal);
            fallbackActivity?.AddEvent(activityEvent);
        }
        catch (Exception)
        {
            // Exporting is best effort: a failing listener or tag value must not break logging.
        }
    }

    private IEnumerable<KeyValuePair<string, object?>> BuildOpenTelemetryTags(
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
            yield return new KeyValuePair<string, object?>("exception.message", _options.EnableSensitiveDataMasking ? _masker.MaskMessage(exceptionMessage) : exceptionMessage);
            yield return new KeyValuePair<string, object?>("exception.stacktrace", FormatException(exception));
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
        DateTime timestamp,
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
            sb.Append($" | Exception: {FormatException(exception)}");

        if (scopes is { Count: > 0 })
            sb.Append($" | Scopes: {string.Join(" > ", scopes)}");

        return sb.ToString();
    }

    private string BuildJsonLogLine(
        DateTime timestamp,
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
            ["exception"] = exception is null ? null : FormatException(exception)
        };

        if (properties is { Count: > 0 })
        {
            var propertyMap = new Dictionary<string, object?>();
            foreach (var kvp in properties)
                propertyMap[kvp.Key] = ToSerializableValue(kvp.Value);
            payload["properties"] = propertyMap;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Serializes one property value up front so a value that cannot be serialized (unsupported type,
    /// throwing getter, too deep) only affects itself: it falls back to <c>ToString()</c>, or to
    /// <c>[unserializable: TypeName]</c> when that throws too.
    /// </summary>
    private object? ToSerializableValue(object? value)
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
            return _options.EnableSensitiveDataMasking && text is not null ? _masker.MaskMessage(text) : text;
        }
    }

    private LogLevel ResolveMinimumLevelForCategory()
    {
        var levels = _options.CategoryMinimumLevels;
        if (levels.Count == 0)
            return _options.MinimumLevel;

        if (levels.TryGetValue(_categoryName, out var exact))
            return exact;

        var current = _categoryName;
        while (true)
        {
            var lastDot = current.LastIndexOf('.');
            if (lastDot <= 0)
                break;

            current = current[..lastDot];
            if (levels.TryGetValue(current, out var matched))
                return matched;
        }

        if (levels.TryGetValue("Default", out var fallback))
            return fallback;

        return _options.MinimumLevel;
    }

    private static string BuildLogLine(
        DateTime timestamp,
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

    private string? ResolveMethodName()
        => ExtractMethodFromScope()
           ?? (_options.ResolveMethodNameFromStackTrace ? ExtractMethodFromStack() : null);

    private string? ExtractMethodFromScope()
    {
        if (!_options.IncludeScopes || _scopeProvider is null)
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

    private string FormatScopeProperty(string key, object? value)
    {
        var raw = SafeToString(value) ?? string.Empty;
        if (!_options.EnableSensitiveDataMasking)
            return $"{key}={raw}";

        var text = _masker.IsSensitiveKey(key)
            ? _masker.MaskValue(key, raw)
            : _masker.MaskMessage(raw);
        return $"{key}={text}";
    }

    private List<string> CollectScopes()
    {
        var scopes = new List<string>();
        _scopeProvider?.ForEachScope((scope, list) =>
        {
            try
            {
                switch (scope)
                {
                    case string s when !string.IsNullOrWhiteSpace(s):
                        list.Add(_options.EnableSensitiveDataMasking ? _masker.MaskMessage(s) : s);
                        break;
                    case IEnumerable<KeyValuePair<string, object?>> kvps:
                    {
                        list.AddRange(from kvp in kvps where !kvp.Key.Equals("MethodName", StringComparison.OrdinalIgnoreCase) select FormatScopeProperty(kvp.Key, kvp.Value));

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
