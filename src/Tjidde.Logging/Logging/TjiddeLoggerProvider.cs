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
    private readonly ICustomerContextAccessor _customerContextAccessor;
    private readonly IMaskedKeysAccessor _maskedKeysAccessor;
    private readonly TjiddeLoggerConfigurationHolder _configuration;
    private readonly IDisposable? _optionsChangeRegistration;
    private readonly object _sync = new();
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
        _customerContextAccessor = customerContextAccessor;
        _maskedKeysAccessor = maskedKeysAccessor;
        _configuration = new TjiddeLoggerConfigurationHolder(
            TjiddeLoggerConfiguration.Create(optionsMonitor.CurrentValue, maskedKeysAccessor));

        // Options changes (for example appsettings.json with reloadOnChange) apply to existing loggers immediately
        _optionsChangeRegistration = optionsMonitor.OnChange(OnOptionsChanged);
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
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _optionsChangeRegistration?.Dispose();
        _loggers.Clear();
        _processor.Dispose();
        _configuration.Current.Dispose();
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
        => new(categoryName, _configuration, _customerContextAccessor, _processor, _scopeProvider);

    private void OnOptionsChanged(TjiddeLoggerOptions options, string? name)
    {
        // Only the default (unnamed) options configure this provider
        if (!string.IsNullOrEmpty(name))
            return;

        TjiddeLoggerConfiguration updated;
        try
        {
            updated = TjiddeLoggerConfiguration.Create(options, _maskedKeysAccessor);
        }
        catch (Exception)
        {
            // Keep logging with the previous configuration rather than fail the configuration reload.
            return;
        }

        TjiddeLoggerConfiguration previous;
        lock (_sync)
        {
            if (_disposed)
            {
                updated.Dispose();
                return;
            }

            previous = _configuration.Exchange(updated);
        }

        previous.Dispose();
    }
}
