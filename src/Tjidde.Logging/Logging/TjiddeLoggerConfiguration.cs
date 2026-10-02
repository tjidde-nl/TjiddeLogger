using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Formatting;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;

namespace Tjidde.Logging.Logging;

/// <summary>
/// Everything a <see cref="TjiddeLogger"/> derives from one set of <see cref="TjiddeLoggerOptions"/>: the options,
/// the masker, the exception formatter and the fallback <see cref="ActivitySource"/>. Immutable and shared by all
/// loggers of a provider; an options change replaces the whole object, so a log call always sees a consistent set.
/// </summary>
internal sealed class TjiddeLoggerConfiguration : IDisposable
{
    public TjiddeLoggerConfiguration(
        TjiddeLoggerOptions options,
        ISensitiveDataMasker masker,
        IExceptionFormatter exceptionFormatter)
    {
        Options = options;
        Masker = masker;
        ExceptionFormatter = exceptionFormatter;
        FallbackActivitySource = string.IsNullOrWhiteSpace(options.OpenTelemetryActivitySourceName)
            ? null
            : new ActivitySource(options.OpenTelemetryActivitySourceName);
    }

    /// <summary>
    /// Builds the configuration for <paramref name="options"/>, with a masker that also applies the runtime keys
    /// from <paramref name="maskedKeysAccessor"/>.
    /// </summary>
    public static TjiddeLoggerConfiguration Create(TjiddeLoggerOptions options, IMaskedKeysAccessor maskedKeysAccessor)
    {
        // The accessor rather than its current keys, so keys added later apply too
        var masker = new SensitiveDataMasker(options.MaskPlaceholder, options.AdditionalSensitiveKeys, maskedKeysAccessor);
        var exceptionFormatter = new ExceptionFormatter(options.IncludeStackTrace, options.IncludeInnerExceptions);
        return new TjiddeLoggerConfiguration(options, masker, exceptionFormatter);
    }

    public TjiddeLoggerOptions Options { get; }

    /// <summary>One masker for all loggers; it is thread-safe.</summary>
    public ISensitiveDataMasker Masker { get; }

    public IExceptionFormatter ExceptionFormatter { get; }

    /// <summary>Source for the short-lived activity used for OpenTelemetry export when no activity is current.</summary>
    public ActivitySource? FallbackActivitySource { get; }

    /// <summary>
    /// The minimum level for <paramref name="categoryName"/>: an exact match in
    /// <see cref="TjiddeLoggerOptions.CategoryMinimumLevels"/>, else the longest matching namespace prefix,
    /// else <c>Default</c>, else <see cref="TjiddeLoggerOptions.MinimumLevel"/>.
    /// Both options are obsolete but still honored for backwards compatibility until 2.0; this filter runs after
    /// the Microsoft.Extensions.Logging filters (<c>Logging:Tjidde:LogLevel</c>), so the stricter one wins.
    /// </summary>
#pragma warning disable CS0618 // MinimumLevel and CategoryMinimumLevels are obsolete; still applied for backwards compatibility
    public LogLevel ResolveMinimumLevel(string categoryName)
    {
        var levels = Options.CategoryMinimumLevels;
        if (levels is null || levels.Count == 0)
            return Options.MinimumLevel;

        if (levels.TryGetValue(categoryName, out var exact))
            return exact;

        var current = categoryName;
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

        return Options.MinimumLevel;
    }
#pragma warning restore CS0618

    /// <summary>
    /// Disposes the fallback activity source. A log call that still uses this configuration stays safe:
    /// a disposed source has no listeners and starts no activities.
    /// </summary>
    public void Dispose() => FallbackActivitySource?.Dispose();
}

/// <summary>
/// Holds the current <see cref="TjiddeLoggerConfiguration"/> of a provider. Loggers read it on every call,
/// so a replacement applies to existing loggers immediately.
/// </summary>
internal sealed class TjiddeLoggerConfigurationHolder
{
    private TjiddeLoggerConfiguration _current;

    public TjiddeLoggerConfigurationHolder(TjiddeLoggerConfiguration initial) => _current = initial;

    public TjiddeLoggerConfiguration Current => Volatile.Read(ref _current);

    /// <summary>Atomically replaces the configuration and returns the previous one.</summary>
    public TjiddeLoggerConfiguration Exchange(TjiddeLoggerConfiguration replacement)
        => Interlocked.Exchange(ref _current, replacement);
}
