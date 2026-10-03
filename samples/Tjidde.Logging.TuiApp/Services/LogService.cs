using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Sinks;
using Tjidde.Logging.TuiApp.Models;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.TuiApp.Services;

public sealed class LogService : ILogService
{
    private readonly ILogger<LogService> _logger;
    private readonly InMemoryLogSink _sink;

    public event Action? LogsChanged;

    public LogService(ILogger<LogService> logger, InMemoryLogSink sink)
    {
        _logger = logger;
        _sink = sink;
        _sink.EntryAdded += (_, _) => LogsChanged?.Invoke();
        _sink.Cleared += (_, _) => LogsChanged?.Invoke();
    }

    public void SendLog(string message, LogLevel level, string? customer, Exception? exception, bool metrics = false)
    {
        CustomerContext.Set(customer);
        try
        {
            if (metrics)
                _logger.LogMetrics(exception, "{Message}", message);
            else
                _logger.Log(level, exception, "{Message}", message);
        }
        finally
        {
            CustomerContext.Clear();
        }
    }

    public IReadOnlyList<string> GetFormattedEntries() =>
        _sink.GetSnapshot()
            .Reverse()
            .Select(e => new LogEntryLine(e).GetStyledString())
            .ToList();

    public void ClearLogs() => _sink.Clear();
}
