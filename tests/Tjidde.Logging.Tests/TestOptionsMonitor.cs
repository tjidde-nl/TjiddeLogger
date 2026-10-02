using Microsoft.Extensions.Options;
using Tjidde.Logging.Options;

namespace Tjidde.Logging.Tests;

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> whose value the test replaces explicitly.</summary>
internal sealed class TestOptionsMonitor : IOptionsMonitor<TjiddeLoggerOptions>
{
    private readonly List<Action<TjiddeLoggerOptions, string?>> _listeners = [];

    public TestOptionsMonitor(TjiddeLoggerOptions initial) => CurrentValue = initial;

    public TjiddeLoggerOptions CurrentValue { get; private set; }

    public int ListenerCount
    {
        get
        {
            lock (_listeners)
                return _listeners.Count;
        }
    }

    public TjiddeLoggerOptions Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<TjiddeLoggerOptions, string?> listener)
    {
        lock (_listeners)
            _listeners.Add(listener);

        return new Registration(() =>
        {
            lock (_listeners)
                _listeners.Remove(listener);
        });
    }

    public void Set(TjiddeLoggerOptions options, string name = "")
    {
        if (name.Length == 0)
            CurrentValue = options;

        Action<TjiddeLoggerOptions, string?>[] listeners;
        lock (_listeners)
            listeners = _listeners.ToArray();

        foreach (var listener in listeners)
            listener(options, name);
    }

    private sealed class Registration(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
