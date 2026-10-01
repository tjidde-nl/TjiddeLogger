using System.Collections.Concurrent;

namespace Tjidde.Logging.Logging;

/// <summary>
/// Writes rendered log lines to the console on a dedicated background thread, so logging calls
/// never block on console I/O. Lines are written in the order they were logged and never interleave.
/// Pending lines are flushed when the processor is disposed or the process exits.
/// </summary>
internal sealed class ConsoleLogProcessor : IDisposable
{
    private const int MaxQueueLength = 2500;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMilliseconds(1500);

    // Shared by all processors so multiple providers never interleave lines or colors.
    private static readonly object ConsoleLock = new();

    private readonly BlockingCollection<LogLine> _queue = new(MaxQueueLength);
    private readonly Thread _thread;
    private readonly object _progressLock = new();
    private long _enqueued;
    private long _processed;

    public ConsoleLogProcessor()
    {
        _thread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "Tjidde.Logging console writer"
        };
        _thread.Start();
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>
    /// Queues a line for writing. Blocks only when the queue is full (back-pressure instead of dropping logs).
    /// After disposal, lines are written synchronously so nothing is lost.
    /// </summary>
    public void Enqueue(string text, ConsoleColor? color)
    {
        // Capture the target now, so output goes where Console.Out pointed when the entry was logged.
        var line = new LogLine(Console.Out, text, color);
        Interlocked.Increment(ref _enqueued);

        if (!_queue.IsAddingCompleted)
        {
            try
            {
                _queue.Add(line);
                return;
            }
            catch (InvalidOperationException)
            {
                // Adding was completed concurrently by Dispose; fall back to a synchronous write.
            }
        }

        Write(line);
        MarkProcessed();
    }

    /// <summary>
    /// Blocks until every line queued before this call has been written, or the timeout elapses.
    /// </summary>
    /// <returns><c>true</c> if all lines were written within the timeout.</returns>
    public bool Flush(TimeSpan timeout)
    {
        var target = Interlocked.Read(ref _enqueued);
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        lock (_progressLock)
        {
            while (Interlocked.Read(ref _processed) < target)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    return false;

                Monitor.Wait(_progressLock, TimeSpan.FromMilliseconds(remaining));
            }
        }

        return true;
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _queue.CompleteAdding();
        _thread.Join(ShutdownTimeout);
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    private void ProcessQueue()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            Write(line);
            MarkProcessed();
        }
    }

    private void MarkProcessed()
    {
        Interlocked.Increment(ref _processed);
        lock (_progressLock)
            Monitor.PulseAll(_progressLock);
    }

    private static void Write(LogLine line)
    {
        try
        {
            lock (ConsoleLock)
            {
                // Skip colors when output is redirected (files, pipes, containers).
                if (line.Color is not { } color || Console.IsOutputRedirected)
                {
                    line.Target.WriteLine(line.Text);
                    return;
                }

                var originalColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = color;
                    line.Target.WriteLine(line.Text);
                }
                finally
                {
                    Console.ForegroundColor = originalColor;
                }
            }
        }
        catch (Exception)
        {
            // Logging must never crash the application, e.g. when the output writer was closed.
        }
    }

    private readonly record struct LogLine(TextWriter Target, string Text, ConsoleColor? Color);
}
