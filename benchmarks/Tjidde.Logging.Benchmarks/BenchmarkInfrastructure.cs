using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tjidde.Logging.Context;
using Tjidde.Logging.Sinks;

namespace Tjidde.Logging.Benchmarks;

/// <summary>An options monitor with fixed options and no change notifications.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>A fixed customer, so every entry carries a customer part.</summary>
internal sealed class FixedCustomerContextAccessor : ICustomerContextAccessor
{
    private readonly string? _customer;

    public FixedCustomerContextAccessor(string? customer) => _customer = customer;

    public string? GetCustomerContext() => _customer;
}

/// <summary>
/// A scope provider that always reports the same scopes: a text scope and a structured scope with a
/// <c>MethodName</c>, as <c>BeginMethodScope</c> creates. Deterministic, unlike an AsyncLocal-based provider
/// whose scopes would have to be pushed inside the measured call.
/// </summary>
internal sealed class FixedScopeProvider : IExternalScopeProvider
{
    private readonly object[] _scopes =
    [
        "Order pipeline",
        new KeyValuePair<string, object?>[]
        {
            new("MethodName", "ProcessOrder"),
            new("OrderId", 12345),
            new("Region", "eu-west")
        }
    ];

    public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
    {
        foreach (var scope in _scopes)
            callback(scope, state);
    }

    public IDisposable Push(object? state) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}

/// <summary>A sink that discards every entry, to measure building the entry without any I/O.</summary>
internal sealed class NoOpSink : ILogSink
{
    public void Write(TjiddeLogEntry entry) { }
}
