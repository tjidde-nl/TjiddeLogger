using Tjidde.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.TuiApp.Models;

public sealed class LogEntryLine
{
    public TjiddeLogEntry Entry { get; }

    public LogEntryLine(TjiddeLogEntry entry) => Entry = entry;

    public string GetStyledString()
    {
        // Metrics entries are recognized by their event ID (MetricsEventId), not by a log level.
        var color = Entry.Level switch
        {
            _ when Entry.IsMetrics => "cyan",
            LogLevel.Trace => "magenta",
            LogLevel.Debug => "gray",
            LogLevel.Information => "white",
            LogLevel.Warning => "yellow",
            LogLevel.Error => "red",
            LogLevel.Critical => "brightred",
            _ => "white"
        };

        var levelStr = FormatLevel();
        return $"[{Entry.Timestamp:HH:mm:ss}] [{{color:{color}}}] {levelStr}: {Entry.Message}";
    }

    public override string ToString()
    {
        var levelStr = FormatLevel();
        return $"[{Entry.Timestamp:HH:mm:ss}] {levelStr}: {Entry.Message}";
    }

    private string FormatLevel() =>
        Entry.IsMetrics ? "METRICS" : Entry.Level.ToString().ToUpper();
}
