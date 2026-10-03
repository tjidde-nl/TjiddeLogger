using System.Text;

namespace Tjidde.Logging.Logging;

/// <summary>
/// One reusable <see cref="StringBuilder"/> per thread for building log lines. A nested log call on the same thread
/// (for example from a value's <c>ToString()</c>) finds the cache empty and gets a new builder, so builders are never
/// shared. Builders that grew beyond <see cref="MaxCachedCapacity"/> are not kept.
/// </summary>
internal static class StringBuilderCache
{
    private const int DefaultCapacity = 256;
    private const int MaxCachedCapacity = 4096;

    [ThreadStatic]
    private static StringBuilder? _cached;

    public static StringBuilder Acquire()
    {
        var sb = _cached;
        if (sb is null)
            return new StringBuilder(DefaultCapacity);

        _cached = null;
        sb.Clear();
        return sb;
    }

    public static string GetStringAndRelease(StringBuilder sb)
    {
        var result = sb.ToString();
        if (sb.Capacity <= MaxCachedCapacity)
            _cached = sb;

        return result;
    }
}
