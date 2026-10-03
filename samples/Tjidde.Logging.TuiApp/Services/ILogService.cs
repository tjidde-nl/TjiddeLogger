using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.TuiApp.Services;

public interface ILogService
{
    /// <summary>Logs <paramref name="message"/> at <paramref name="level"/>, or as a metrics entry (LogMetrics) when <paramref name="metrics"/> is set.</summary>
    void SendLog(string message, LogLevel level, string? customer, Exception? exception, bool metrics = false);
    IReadOnlyList<string> GetFormattedEntries();
    void ClearLogs();
    event Action? LogsChanged;
}
