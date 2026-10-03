namespace Tjidde.Logging.Sinks;

/// <summary>
/// Passes entries to the registered <see cref="ILogSink"/>s, synchronously and in registration order.
/// A failing sink is ignored and does not affect the others.
/// </summary>
internal sealed class LogSinkDispatcher
{
    public static readonly LogSinkDispatcher Empty = new(null);

    // Set while this thread is inside a sink, so an entry logged by a sink is not dispatched again (no recursion).
    [ThreadStatic]
    private static bool _dispatching;

    private readonly ILogSink[] _sinks;

    public LogSinkDispatcher(IEnumerable<ILogSink>? sinks)
    {
        var unique = new List<ILogSink>();
        if (sinks is not null)
        {
            foreach (var sink in sinks)
            {
                // The same instance can be registered twice (e.g. AddTjiddeSink and AddSingleton); write to it once.
                if (sink is not null && !unique.Exists(existing => ReferenceEquals(existing, sink)))
                    unique.Add(sink);
            }
        }

        _sinks = unique.ToArray();
    }

    /// <summary>Whether entries should be built for the sinks at all: there are sinks and this is not a nested call.</summary>
    public bool IsActive => _sinks.Length > 0 && !_dispatching;

    public void Dispatch(TjiddeLogEntry entry)
    {
        if (!IsActive)
            return;

        _dispatching = true;
        try
        {
            foreach (var sink in _sinks)
            {
                try
                {
                    sink.Write(entry);
                }
                catch (Exception)
                {
                    // A sink must never break logging or keep the other sinks from receiving the entry.
                }
            }
        }
        finally
        {
            _dispatching = false;
        }
    }
}
