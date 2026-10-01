using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.TuiApp.Services;

public interface ILogService
{
    void SendLog(string message, LogLevel level, string? customer, Exception? exception);
    IReadOnlyList<string> GetFormattedEntries();
    void ClearLogs();
    event Action? LogsChanged;
}
