namespace Tjidde.Logging.Options;

/// <summary>
/// Supported output formats for log entries.
/// </summary>
public enum TjiddeLogOutputFormat
{
    /// <summary>One human-readable line per entry: <c>YYYY-MM-DD: HH:mm:ss: [LEVEL] Class=&gt;... : message</c>.</summary>
    Text = 0,

    /// <summary>One JSON object per entry, for log collectors.</summary>
    Json = 1
}
