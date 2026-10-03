namespace Tjidde.Logging.Sinks;

/// <summary>
/// A destination for Tjidde log entries besides (or instead of) the console, for example an in-memory buffer
/// for a UI, a file or a test assertion. Register sinks in DI (<c>services.AddSingleton&lt;ILogSink, MySink&gt;()</c>)
/// or with <c>AddTjiddeSink</c>; the provider passes every entry it writes to every sink.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Write"/> is called synchronously on the thread that logs, after the entry is rendered and masked.
/// Keep it fast and non-blocking: queue slow work (network, disk) yourself.
/// </para>
/// <para>
/// Implementations must be thread-safe: several threads can log at the same time.
/// An exception thrown by a sink is caught and ignored; it never reaches the caller and never stops other sinks
/// or the console from receiving the entry. Entries logged from inside <see cref="Write"/> are not passed to the
/// sinks again (they still go to the console), so a sink that logs cannot recurse.
/// </para>
/// <para>
/// Sinks are resolved when the logger provider is created, so a sink must not depend on <c>ILogger&lt;T&gt;</c>
/// or <c>ILoggerFactory</c> through its constructor (that is a circular dependency). The container disposes sinks
/// it created; the provider does not.
/// </para>
/// </remarks>
public interface ILogSink
{
    /// <summary>Receives one log entry. Called synchronously; must be fast and thread-safe.</summary>
    /// <param name="entry">The entry; contains only masked data.</param>
    void Write(TjiddeLogEntry entry);
}
