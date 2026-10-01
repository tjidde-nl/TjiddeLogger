using Tjidde.Logging.Context;
using Tjidde.Logging.TuiApp.Logging;
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
        _sink.OnChanged += () => LogsChanged?.Invoke();
    }

    public void SendLog(string message, LogLevel level, string? customer, Exception? exception)
    {
        CustomerContext.Set(customer);
        try
        {
            _logger.Log(level, exception, "{Message}", message);
        }
        finally
        {
            CustomerContext.Clear();
        }
    }

    public IReadOnlyList<string> GetFormattedEntries() =>
        _sink.GetEntries()
            .Reverse()
            .Select(e => new LogEntryLine(e).GetStyledString())
            .ToList();

    public void ClearLogs() => _sink.Clear();
}
