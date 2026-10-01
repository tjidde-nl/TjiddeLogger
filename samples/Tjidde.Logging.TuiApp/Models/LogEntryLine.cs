using Tjidde.Logging.TuiApp.Logging;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.TuiApp.Models;

public sealed class LogEntryLine
{
    public LogEntry Entry { get; }

    public LogEntryLine(LogEntry entry) => Entry = entry;

    public string GetStyledString()
    {
        var color = Entry.Level switch
        {
            LogLevel.Trace => "magenta",
            LogLevel.Debug => "gray",
            LogLevel.Information => "white",
            LogLevel.Warning => "yellow",
            LogLevel.Error => "red",
            LogLevel.Critical => "brightred",
            _ when (int)Entry.Level == 700 => "cyan",
            _ => "white"
        };

        var levelStr = FormatLevel();
        return $"[{Entry.Timestamp:HH:mm:ss}] [{{color:{color}}}] {levelStr}: {Entry.FormattedMessage}";
    }

    public override string ToString()
    {
        var levelStr = FormatLevel();
        return $"[{Entry.Timestamp:HH:mm:ss}] {levelStr}: {Entry.FormattedMessage}";
    }

    private string FormatLevel() =>
        Entry.Level == (LogLevel)700 ? "METRICS" : Entry.Level.ToString().ToUpper();
}
