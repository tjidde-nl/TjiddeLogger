using System.Collections;
using System.Globalization;
using System.Text;

namespace Tjidde.Logging.Masking;

/// <summary>
/// Renders a message template such as <c>"Login {User} with {Password}"</c> from a set of named values,
/// formatting them the same way Microsoft.Extensions.Logging does. Used to rebuild a log message from
/// masked values, because the framework's own formatter inserts the raw (unmasked) values.
/// </summary>
internal static class MessageTemplateRenderer
{
    private const string NullValue = "(null)";

    /// <summary>
    /// Renders <paramref name="template"/>. The n-th occurrence of a placeholder name uses the n-th value with that name.
    /// </summary>
    /// <returns><c>false</c> if the template references a missing value or cannot be formatted.</returns>
    public static bool TryRender(string template, IReadOnlyList<KeyValuePair<string, object?>> values, out string rendered)
    {
        rendered = string.Empty;
        var format = new StringBuilder(template.Length);
        var args = new List<object>();
        var used = new bool[values.Count];

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            var isEscaped = i + 1 < template.Length && template[i + 1] == c;

            if (c == '}' || (c == '{' && isEscaped))
            {
                format.Append(c).Append(c);
                if (isEscaped)
                    i++;
                continue;
            }

            if (c != '{')
            {
                format.Append(c);
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
                return false;

            // Placeholder syntax: {Name[,alignment][:format]}
            var hole = template.Substring(i + 1, close - i - 1);
            var nameEnd = hole.IndexOfAny([',', ':']);
            var name = nameEnd < 0 ? hole : hole[..nameEnd];

            var index = FindUnusedValue(values, used, name);
            if (index < 0)
                return false;

            used[index] = true;
            format.Append('{').Append(args.Count).Append(nameEnd < 0 ? string.Empty : hole[nameEnd..]).Append('}');
            args.Add(FormatValue(values[index].Value));
            i = close;
        }

        try
        {
            rendered = string.Format(CultureInfo.InvariantCulture, format.ToString(), args.ToArray());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static int FindUnusedValue(IReadOnlyList<KeyValuePair<string, object?>> values, bool[] used, string name)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (!used[i] && values[i].Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    // Mirrors Microsoft.Extensions.Logging: null becomes "(null)" and collections are joined with ", ".
    // A value whose enumeration or ToString() throws becomes "[unserializable: TypeName]" instead of failing.
    private static object FormatValue(object? value)
    {
        try
        {
            return value switch
            {
                null => NullValue,
                string text => text,
                IEnumerable items => string.Join(", ", items.Cast<object?>().Select(item => item ?? NullValue)),
                IFormattable => value,
                _ => value.ToString() ?? string.Empty
            };
        }
        catch (Exception)
        {
            return $"[unserializable: {value!.GetType().FullName}]";
        }
    }
}
