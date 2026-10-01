using System.Text;

namespace Tjidde.Logging.Formatting;

/// <summary>
/// Formats exceptions into a structured, readable single-line string suitable for log output.
/// </summary>
public sealed class ExceptionFormatter : IExceptionFormatter
{
    private readonly bool _includeStackTrace;
    private readonly bool _includeInnerExceptions;

    /// <summary>
    /// Initializes a new instance of <see cref="ExceptionFormatter"/>.
    /// </summary>
    public ExceptionFormatter(bool includeStackTrace = true, bool includeInnerExceptions = true)
    {
        _includeStackTrace = includeStackTrace;
        _includeInnerExceptions = includeInnerExceptions;
    }

    /// <inheritdoc />
    public string Format(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var sb = new StringBuilder();
        AppendException(sb, exception, depth: 0);
        return sb.ToString().TrimEnd();
    }

    private void AppendException(StringBuilder sb, Exception exception, int depth)
    {
        var prefix = depth > 0 ? " -> " : string.Empty;

        if (sb.Length > 0 && depth == 0)
            sb.Append(' ');

        sb.Append($"{prefix}[{exception.GetType().FullName}: {exception.Message}");

        if (_includeStackTrace && !string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            var frames = exception.StackTrace
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim());
            sb.Append($" | StackTrace: {string.Join(" | ", frames)}");
        }

        sb.Append(']');

        if (_includeInnerExceptions && exception.InnerException is not null)
            AppendException(sb, exception.InnerException, depth + 1);
    }
}
