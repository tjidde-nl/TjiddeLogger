using System.Collections.Concurrent;
using Tjidde.Logging.Context;
using Tjidde.Logging.Formatting;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tjidde.Logging.Logging;

/// <summary>
/// Provides instances of <see cref="TjiddeLogger"/> for each log category.
/// Registered as a singleton <see cref="ILoggerProvider"/> in the DI container.
/// </summary>
[ProviderAlias("Tjidde")]
public sealed class TjiddeLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IOptionsMonitor<TjiddeLoggerOptions> _optionsMonitor;
    private readonly ICustomerContextAccessor _customerContextAccessor;
    private readonly IMaskedKeysAccessor _maskedKeysAccessor;
    private readonly ConcurrentDictionary<string, TjiddeLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConsoleLogProcessor _processor = new();
    private IExternalScopeProvider _scopeProvider = NoopExternalScopeProvider.Instance;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="TjiddeLoggerProvider"/>.
    /// </summary>
    public TjiddeLoggerProvider(
        IOptionsMonitor<TjiddeLoggerOptions> optionsMonitor,
        ICustomerContextAccessor customerContextAccessor,
        IMaskedKeysAccessor maskedKeysAccessor)
    {
        _optionsMonitor = optionsMonitor;
        _customerContextAccessor = customerContextAccessor;
        _maskedKeysAccessor = maskedKeysAccessor;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => CreateLoggerInstance(name));
    }

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;

        // Update all existing loggers with the new scope provider
        foreach (var logger in _loggers.Values)
            logger.SetScopeProvider(scopeProvider);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _loggers.Clear();
            _processor.Dispose();
            _disposed = true;
        }
    }

    /// <summary>A no-op scope provider used before an external one is assigned.</summary>
    private sealed class NoopExternalScopeProvider : IExternalScopeProvider
    {
        public static readonly NoopExternalScopeProvider Instance = new();
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) { }
        public IDisposable Push(object? state) => NullScope.Instance;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private TjiddeLogger CreateLoggerInstance(string categoryName)
    {
        var options = _optionsMonitor.CurrentValue;
        // Pass the accessor rather than its current keys, so keys added later reach this logger too
        var masker = new SensitiveDataMasker(
            options.MaskPlaceholder,
            options.AdditionalSensitiveKeys,
            _maskedKeysAccessor);
        var exceptionFormatter = new ExceptionFormatter(options.IncludeStackTrace, options.IncludeInnerExceptions);

        return new TjiddeLogger(
            categoryName,
            options,
            _customerContextAccessor,
            masker,
            exceptionFormatter,
            _processor,
            _scopeProvider);
    }
}
