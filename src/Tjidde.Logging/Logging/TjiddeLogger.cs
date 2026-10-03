using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Formatting;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Tjidde.Logging.Sinks;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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
    private readonly LogSinkDispatcher _sinks;
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
    /// Entries go to the console (unless <see cref="TjiddeLoggerOptions.WriteToConsole"/> is off) and to <paramref name="sinks"/>.
    /// </summary>
    public TjiddeLogger(
        string categoryName,
        TjiddeLoggerConfigurationHolder configuration,
        ICustomerContextAccessor customerContextAccessor,
        ConsoleLogProcessor processor,
        IExternalScopeProvider? scopeProvider,
        TimeProvider? timeProvider = null,
        LogSinkDispatcher? sinks = null)
    {
        _sinks = sinks ?? LogSinkDispatcher.Empty;
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
        TimeProvider? timeProvider = null,
        LogSinkDispatcher? sinks = null)
        : this(
            categoryName,
            new TjiddeLoggerConfigurationHolder(new TjiddeLoggerConfiguration(options, masker, exceptionFormatter)),
            customerContextAccessor,
            processor,
            scopeProvider,
            timeProvider,
            sinks)
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

        // One pass over the scopes for both the MethodName scope and the scope texts (when scopes are included).
        string? methodName;
        List<string>? scopes = null;
        if (config.Options.IncludeScopes && _scopeProvider is not null)
            (methodName, scopes) = ReadScopes(config, _scopeProvider);
        else
            methodName = null;

        if (methodName is null && config.Options.ResolveMethodNameFromStackTrace)
            methodName = ExtractMethodFromStack();

        // Allocated only when a property is actually masked.
        List<string>? maskedKeyNames = null;
        List<KeyValuePair<string, object?>>? properties = null;
        string? originalFormat = null;

        // Collect structured properties once, masking sensitive values so they never
        // reach any output (text, JSON or OpenTelemetry) unmasked.
        if (state is IEnumerable<KeyValuePair<string, object?>> structuredState)
        {
            // FormattedLogValues and LoggerMessage state know their count ({OriginalFormat} included).
            properties = structuredState is IReadOnlyCollection<KeyValuePair<string, object?>> { Count: > 0 } sized
                ? new List<KeyValuePair<string, object?>>(sized.Count)
                : new List<KeyValuePair<string, object?>>();
            foreach (var kvp in structuredState)
            {
                if (kvp.Key.Equals("{OriginalFormat}", StringComparison.Ordinal))
                {
                    originalFormat = kvp.Value as string;
                    continue;
                }

                properties.Add(new KeyValuePair<string, object?>(kvp.Key, SafeMaskPropertyValue(config, kvp.Key, kvp.Value, ref maskedKeyNames)));
            }
        }

        message = RenderMessage(config, state, exception, formatter, originalFormat, properties, maskedKeyNames is not null);

        // Formatted (and masked) once, for the rendered line and the sinks.
        var formattedException = exception is null ? null : FormatException(config, exception);

        var rendered = config.Options.OutputFormat == TjiddeLogOutputFormat.Json
            ? BuildJsonLogLine(config, now, logLevel, eventId, methodName, customer, message, formattedException, scopes, maskedKeyNames, properties)
            : BuildTextLogLine(now, logLevel, eventId, methodName, customer, message, formattedException, scopes, maskedKeyNames);

        ExportToOpenTelemetry(config, now, logLevel, eventId, methodName, customer, message, exception, scopes, maskedKeyNames, properties);

        Emit(config, now, logLevel, eventId, methodName, customer, message, formattedException, rendered);
    }

    /// <summary>
    /// Writes the rendered entry to the console (when <see cref="TjiddeLoggerOptions.WriteToConsole"/> is on)
    /// and passes it to the sinks. Only masked data reaches either.
    /// </summary>
    private void Emit(
        TjiddeLoggerConfiguration config,
        DateTimeOffset timestamp,
        LogLevel logLevel,
        EventId eventId,
        string? methodName,
        string? customer,
        string message,
        string? formattedException,
        string rendered)
    {
        var format = config.Options.OutputFormat;
        if (config.Options.WriteToConsole)
            _processor.Enqueue(rendered, format == TjiddeLogOutputFormat.Text ? GetColor(logLevel, eventId) : null);

        if (!_sinks.IsActive)
            return;

        _sinks.Dispatch(new TjiddeLogEntry
        {
            Timestamp = timestamp,
            Level = logLevel,
            EventId = eventId,
            Category = _categoryName,
            ClassName = _className,
            MethodName = methodName,
            Customer = customer,
            Message = message,
            FormattedException = formattedException,
            RenderedLine = rendered,
            OutputFormat = format
        });
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

            Emit(config, now, logLevel, eventId, methodName: null, customer: null, message, formattedException: null, line);
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
    private static string? FallbackMessage<TState>(TjiddeLoggerConfiguration config, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
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

    private static string? TryMask(TjiddeLoggerConfiguration config, string? text)
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

    private static string RenderMessage<TState>(
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

    private static object? SafeMaskPropertyValue(TjiddeLoggerConfiguration config, string key, object? value, ref List<string>? maskedKeyNames)
    {
        try
        {
            return MaskPropertyValue(config, key, value, ref maskedKeyNames);
        }
        catch (Exception)
        {
            // Only reachable for sensitive keys (ToString threw); never fall back to the raw value there.
            return value is null ? null : Unserializable(value);
        }
    }

    private static object? MaskPropertyValue(TjiddeLoggerConfiguration config, string key, object? value, ref List<string>? maskedKeyNames)
    {
        if (!config.Options.EnableSensitiveDataMasking)
            return value;

        if (config.Masker.IsSensitiveKey(key))
        {
            (maskedKeyNames ??= []).Add(key);
            return config.Masker.MaskValue(key, value?.ToString() ?? string.Empty);
        }

        // Non-sensitive keys can still carry sensitive literals (e.g. "password=..." or dynamic keys).
        return value is string text ? config.Masker.MaskMessage(text) : value;
    }

    private static string FormatException(TjiddeLoggerConfiguration config, Exception exception)
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
        List<string>? maskedKeyNames,
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
        List<string>? maskedKeyNames,
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

        if (maskedKeyNames is { Count: > 0 })
            yield return new KeyValuePair<string, object?>("log.masked_fields", string.Join(",", maskedKeyNames));

        if (properties is not null)
        {
            foreach (var kvp in properties)
                yield return new KeyValuePair<string, object?>($"log.property.{kvp.Key}", kvp.Value);
        }
    }

    private string BuildTextLogLine(
        DateTimeOffset timestamp,
        LogLevel logLevel,
        EventId eventId,
        string? methodName,
        string? customer,
        string message,
        string? formattedException,
        List<string>? scopes,
        List<string>? maskedKeyNames)
    {
        // Written straight into one (reused) builder: no intermediate strings for the date, time and parts.
        var sb = StringBuilderCache.Acquire();
        AppendLogLine(sb, timestamp, FormatLogLevel(logLevel, eventId), _className, methodName, customer, message);

        if (maskedKeyNames is { Count: > 0 })
            AppendJoined(sb.Append(" [Masked: "), ", ", maskedKeyNames).Append(']');

        if (formattedException is not null)
            sb.Append(" | Exception: ").Append(formattedException);

        if (scopes is { Count: > 0 })
            AppendJoined(sb.Append(" | Scopes: "), " > ", scopes);

        return StringBuilderCache.GetStringAndRelease(sb);
    }

    private static StringBuilder AppendJoined(StringBuilder sb, string separator, List<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
                sb.Append(separator);
            sb.Append(values[i]);
        }

        return sb;
    }

    private string BuildJsonLogLine(
        TjiddeLoggerConfiguration config,
        DateTimeOffset timestamp,
        LogLevel logLevel,
        EventId eventId,
        string? methodName,
        string? customer,
        string message,
        string? formattedException,
        List<string>? scopes,
        List<string>? maskedKeyNames,
        List<KeyValuePair<string, object?>>? properties)
    {
        // Each property value is serialized on its own first, so a value that cannot be serialized only affects itself.
        object?[]? values = null;
        if (properties is { Count: > 0 })
        {
            values = new object?[properties.Count];
            for (var i = 0; i < values.Length; i++)
                values[i] = ToSerializableValue(config, properties[i].Value);
        }

        var line = new JsonLine(
            timestamp.ToString("O"), message, FormatLogLevel(logLevel, eventId), _categoryName, _className, eventId,
            customer, methodName, scopes is { Count: > 0 } ? scopes : null, maskedKeyNames is { Count: > 0 } ? maskedKeyNames : null,
            formattedException, properties, values);

        try
        {
            return WriteJsonLine(line);
        }
        catch (Exception)
        {
            // The direct writer failed (for example a value nested too deeply): serialize as before, which either
            // produces the same line or fails the same way.
            return SerializeJsonLine(line);
        }
    }

    /// <summary>The parts of a JSON line; <c>Values</c> holds the serializable value of each property.</summary>
    private readonly record struct JsonLine(
        string Timestamp,
        string Message,
        string Level,
        string Category,
        string ClassName,
        EventId EventId,
        string? Customer,
        string? Method,
        List<string>? Scopes,
        List<string>? MaskedFields,
        string? Exception,
        List<KeyValuePair<string, object?>>? Properties,
        object?[]? Values);

    private static readonly JsonWriterOptions JsonLineWriterOptions = new()
    {
        // As JsonSerializer writes with JsonOptions: default encoder, not indented, same maximum depth.
        Encoder = JsonOptions.Encoder,
        Indented = JsonOptions.WriteIndented,
        MaxDepth = JsonOptions.MaxDepth
    };

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _cachedJsonBuffer;

    /// <summary>
    /// Writes the line with a <see cref="Utf8JsonWriter"/>: the same JSON as <see cref="SerializeJsonLine"/>,
    /// without building dictionaries and without the serializer's per-value type lookups.
    /// </summary>
    private static string WriteJsonLine(in JsonLine line)
    {
        // Reused per thread; nothing in here calls user code, so a nested log call cannot reach it while in use.
        var buffer = _cachedJsonBuffer ?? new ArrayBufferWriter<byte>(512);
        _cachedJsonBuffer = null;
        buffer.Clear();

        try
        {
            using (var writer = new Utf8JsonWriter(buffer, JsonLineWriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("@timestamp", line.Timestamp);
                writer.WriteString("message", line.Message);
                writer.WriteString("level", line.Level);
                writer.WriteString("category", line.Category);
                writer.WriteString("class", line.ClassName);
                writer.WriteNumber("eventId", line.EventId.Id);
                writer.WriteString("eventName", line.EventId.Name);
                writer.WriteString("customer", line.Customer);
                writer.WriteString("method", line.Method);
                WriteStringArray(writer, "scopes", line.Scopes);
                WriteStringArray(writer, "maskedFields", line.MaskedFields);
                writer.WriteString("exception", line.Exception);

                if (line.Properties is { Count: > 0 } properties)
                {
                    writer.WriteStartObject("properties");
                    for (var i = 0; i < properties.Count; i++)
                    {
                        // A repeated name keeps its first position and its last value, as the dictionary did.
                        var key = properties[i].Key;
                        if (IndexOfKey(properties, key, 0, i) >= 0)
                            continue;

                        var last = i;
                        for (var j = i + 1; j < properties.Count; j++)
                        {
                            if (string.Equals(properties[j].Key, key, StringComparison.Ordinal))
                                last = j;
                        }

                        writer.WritePropertyName(key);
                        WriteValue(writer, line.Values![last]);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        finally
        {
            if (buffer.Capacity <= 16 * 1024)
                _cachedJsonBuffer = buffer;
        }
    }

    private static int IndexOfKey(List<KeyValuePair<string, object?>> properties, string key, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (string.Equals(properties[i].Key, key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, List<string>? values)
    {
        if (values is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    /// <summary>Writes a value produced by <see cref="ToSerializableValue"/>.</summary>
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            default:
                // Not produced by ToSerializableValue; written by the serializer, as before.
                JsonSerializer.Serialize(writer, value, value.GetType(), JsonOptions);
                break;
        }
    }

    /// <summary>The original way to write the line: dictionaries serialized by <see cref="JsonSerializer"/>.</summary>
    private static string SerializeJsonLine(in JsonLine line)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@timestamp"] = line.Timestamp,
            ["message"] = line.Message,
            ["level"] = line.Level,
            ["category"] = line.Category,
            ["class"] = line.ClassName,
            ["eventId"] = line.EventId.Id,
            ["eventName"] = line.EventId.Name,
            ["customer"] = line.Customer,
            ["method"] = line.Method,
            ["scopes"] = line.Scopes,
            ["maskedFields"] = line.MaskedFields,
            ["exception"] = line.Exception
        };

        if (line.Properties is { Count: > 0 } properties)
        {
            var propertyMap = new Dictionary<string, object?>();
            for (var i = 0; i < properties.Count; i++)
                propertyMap[properties[i].Key] = line.Values![i];
            payload["properties"] = propertyMap;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Serializes one property value up front so a value that cannot be serialized (unsupported type,
    /// throwing getter, too deep) only affects itself: it falls back to <c>ToString()</c>, or to
    /// <c>[unserializable: TypeName]</c> when that throws too.
    /// </summary>
    private static object? ToSerializableValue(TjiddeLoggerConfiguration config, object? value)
    {
        // Values that serialize to themselves are written directly, which gives the same JSON as their element.
        // Strings with surrogates go through the serializer, whose round trip may change unpaired surrogates.
        switch (value)
        {
            case null:
                return null;
            case string text when !ContainsSurrogate(text):
            case int or long or bool:
                return value;
        }

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

    private static bool ContainsSurrogate(string text)
    {
#if NET8_0_OR_GREATER
        return text.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') >= 0;
#else
        foreach (var c in text)
        {
            if (char.IsSurrogate(c))
                return true;
        }

        return false;
#endif
    }

    /// <summary>
    /// Appends <c>{date}: {time}: [{level}] Class=>{className}[ Method=>{method}]: [Client=>{customer}: ]{message}</c>.
    /// The date and time are formatted with the current culture, like <see cref="DateTimeOffset.ToString(string)"/>.
    /// </summary>
    private static void AppendLogLine(
        StringBuilder sb,
        DateTimeOffset timestamp,
        string level,
        string className,
        string? methodName,
        string? customer,
        string message)
    {
        sb.Append(timestamp.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture))
            .Append(": ")
            .Append(timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture))
            .Append(": [")
            .Append(level)
            .Append("] Class=>")
            .Append(className);

        if (!string.IsNullOrWhiteSpace(methodName))
            sb.Append(" Method=>").Append(methodName);

        sb.Append(": ");

        if (!string.IsNullOrWhiteSpace(customer))
            sb.Append("Client=>").Append(customer).Append(": ");

        sb.Append(message);
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

    /// <summary>
    /// Reads the active scopes once: the method name from the innermost <c>MethodName</c> scope value, and the scope
    /// texts for the output (masked, without <c>MethodName</c>). Scopes is <see langword="null"/> when no scope adds text.
    /// </summary>
    private static (string? MethodName, List<string>? Scopes) ReadScopes(TjiddeLoggerConfiguration config, IExternalScopeProvider scopeProvider)
    {
        // Reused per thread so a call without scopes allocates nothing; a nested log call (from a scope value's
        // ToString) finds the cache empty and uses its own reader.
        var reader = ScopeReader.Cached ?? new ScopeReader();
        ScopeReader.Cached = null;

        reader.Config = config;
        try
        {
            scopeProvider.ForEachScope(static (scope, r) => r.Read(scope), reader);
            return (reader.MethodName, reader.Scopes);
        }
        finally
        {
            reader.Reset();
            ScopeReader.Cached = reader;
        }
    }

    private sealed class ScopeReader
    {
        [ThreadStatic]
        public static ScopeReader? Cached;

        public TjiddeLoggerConfiguration Config = null!;
        public string? MethodName;
        public List<string>? Scopes;

        public void Reset()
        {
            Config = null!;
            MethodName = null;
            Scopes = null;
        }

        public void Read(object? scope)
        {
            // The method name: an enumeration that throws here fails the entry (it is not caught), as before.
            if (scope is IEnumerable<KeyValuePair<string, object?>> methodScope)
            {
                foreach (var kvp in methodScope)
                {
                    if (kvp.Key.Equals("MethodName", StringComparison.OrdinalIgnoreCase) && kvp.Value is string m)
                        MethodName = m;
                }
            }

            // The scope text: a scope whose enumeration throws is skipped instead of failing the whole entry.
            try
            {
                switch (scope)
                {
                    case string s when !string.IsNullOrWhiteSpace(s):
                        (Scopes ??= []).Add(Config.Options.EnableSensitiveDataMasking ? Config.Masker.MaskMessage(s) : s);
                        break;
                    case IEnumerable<KeyValuePair<string, object?>> kvps:
                    {
                        foreach (var kvp in kvps)
                        {
                            if (!kvp.Key.Equals("MethodName", StringComparison.OrdinalIgnoreCase))
                                (Scopes ??= []).Add(FormatScopeProperty(Config, kvp.Key, kvp.Value));
                        }

                        break;
                    }
                }
            }
            catch (Exception)
            {
                (Scopes ??= []).Add($"[unserializable scope: {scope?.GetType().FullName}]");
            }
        }
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

    private static string FormatScopeProperty(TjiddeLoggerConfiguration config, string key, object? value)
    {
        var raw = SafeToString(value) ?? string.Empty;
        if (!config.Options.EnableSensitiveDataMasking)
            return $"{key}={raw}";

        var text = config.Masker.IsSensitiveKey(key)
            ? config.Masker.MaskValue(key, raw)
            : config.Masker.MaskMessage(raw);
        return $"{key}={text}";
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
