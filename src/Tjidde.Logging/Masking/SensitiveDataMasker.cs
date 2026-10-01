using System.Text.RegularExpressions;

namespace Tjidde.Logging.Masking;

/// <summary>
/// Masks sensitive values in log messages and structured log state.
/// This implementation reduces risk significantly but is not guaranteed to catch
/// every possible form of sensitive data leakage. Always review log output in new contexts.
/// </summary>
public sealed class SensitiveDataMasker : ISensitiveDataMasker
{
    private static readonly string[] DefaultSensitiveKeys =
    [
        "password",
        "wachtwoord",
        "token",
        "accesstoken",
        "access_token",
        "refreshtoken",
        "refresh_token",
        "secret",
        "clientsecret",
        "client_secret",
        "apikey",
        "api_key",
        "x-api-key",
        "authorization",
        "bearer",
        "cookie",
        "set-cookie"
    ];

    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly string _placeholder;
    private readonly TimeSpan _matchTimeout;
    private readonly KeySet _keys;
    private readonly IMaskedKeysAccessor? _runtimeKeysAccessor;
    private KeySet? _runtimeKeys;

    /// <summary>
    /// Initializes a new instance of <see cref="SensitiveDataMasker"/>.
    /// </summary>
    /// <param name="placeholder">The string used to replace sensitive values.</param>
    /// <param name="additionalKeys">Additional keys to treat as sensitive beyond the built-in defaults.</param>
    /// <param name="dynamicKeys">Keys provided dynamically at runtime.</param>
    public SensitiveDataMasker(
        string placeholder = "[REDACTED]",
        IEnumerable<string>? additionalKeys = null,
        IEnumerable<string>? dynamicKeys = null)
        : this(placeholder, additionalKeys, dynamicKeys, runtimeKeysAccessor: null, DefaultMatchTimeout)
    {
    }

    /// <summary>
    /// Initializes a masker that also applies the keys from <paramref name="runtimeKeysAccessor"/>.
    /// Changes to those keys are picked up on the next call, so they apply to existing loggers too.
    /// </summary>
    internal SensitiveDataMasker(string placeholder, IEnumerable<string>? additionalKeys, IMaskedKeysAccessor runtimeKeysAccessor)
        : this(placeholder, additionalKeys, dynamicKeys: null, runtimeKeysAccessor, DefaultMatchTimeout)
    {
    }

    internal SensitiveDataMasker(
        string placeholder,
        IEnumerable<string>? additionalKeys,
        IEnumerable<string>? dynamicKeys,
        IMaskedKeysAccessor? runtimeKeysAccessor,
        TimeSpan matchTimeout)
    {
        _placeholder = placeholder;
        _matchTimeout = matchTimeout;
        _runtimeKeysAccessor = runtimeKeysAccessor;

        var literals = dynamicKeys?.ToArray() ?? [];
        _keys = new KeySet(
            DefaultSensitiveKeys.Concat(additionalKeys ?? []).Concat(literals),
            literals,
            placeholder,
            matchTimeout);
    }

    /// <inheritdoc />
    public bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var normalized = NormalizeKey(key);
        return _keys.Contains(normalized) || (CurrentRuntimeKeys()?.Contains(normalized) ?? false);
    }

    private static string NormalizeKey(string key) => key.Replace("-", "").Replace("_", "");

    /// <inheritdoc />
    public string MaskMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        var runtimeKeys = CurrentRuntimeKeys();

        var result = _keys.MaskPatterns(message);
        if (runtimeKeys is not null)
            result = runtimeKeys.MaskPatterns(result);

        // Dynamic keys are also masked as literal values anywhere in the message
        result = _keys.MaskLiterals(result);
        if (runtimeKeys is not null)
            result = runtimeKeys.MaskLiterals(result);

        return result;
    }

    /// <inheritdoc />
    public string MaskValue(string key, string value)
    {
        if (IsSensitiveKey(key))
            return _placeholder;

        return value;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> MaskDictionary(IEnumerable<KeyValuePair<string, object?>> properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in properties)
        {
            var stringValue = kvp.Value?.ToString() ?? string.Empty;
            result[kvp.Key] = IsSensitiveKey(kvp.Key) ? _placeholder : stringValue;
        }
        return result;
    }

    private KeySet? CurrentRuntimeKeys()
    {
        if (_runtimeKeysAccessor is null)
            return null;

        var keys = _runtimeKeysAccessor.GetKeys();
        var current = Volatile.Read(ref _runtimeKeys);
        if (current is not null && current.IsBuiltFrom(keys))
            return current;

        // The keys changed since the last call. Concurrent rebuilds produce equal sets, so racing is harmless.
        var rebuilt = new KeySet(keys, keys, _placeholder, _matchTimeout, source: keys);
        Volatile.Write(ref _runtimeKeys, rebuilt);
        return rebuilt;
    }

    /// <summary>
    /// A set of sensitive keys together with the patterns that mask their values in free text.
    /// </summary>
    private sealed class KeySet
    {
        // HTTP auth schemes stay visible so the credentials after them are masked:
        // "Authorization: Bearer abc" becomes "Authorization: Bearer [REDACTED]".
        private const string AuthSchemes = "bearer|basic|digest|negotiate|ntlm";

        // Not RegexOptions.Compiled: every logger builds its own patterns, and the JIT time of a compiled
        // pattern's first match counts toward the match timeout, which made busy apps hit timeouts.
        private const RegexOptions PatternOptions = RegexOptions.IgnoreCase;

        private readonly HashSet<string> _normalizedKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(Regex Pattern, MatchEvaluator Mask)> _patterns = [];
        private readonly string[] _literals;
        private readonly string _placeholder;
        private readonly IReadOnlyCollection<string>? _source;
        private readonly HashSet<string> _sourceKeys;

        public KeySet(
            IEnumerable<string> keys,
            IEnumerable<string> literals,
            string placeholder,
            TimeSpan matchTimeout,
            IReadOnlyCollection<string>? source = null)
        {
            _placeholder = placeholder;
            _literals = literals.Where(literal => !string.IsNullOrWhiteSpace(literal)).ToArray();
            _source = source;
            _sourceKeys = new HashSet<string>(source ?? [], StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                // A blank key would turn every "name: value" in a message into a match
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                _normalizedKeys.Add(NormalizeKey(key));
                var escaped = Regex.Escape(key);

                // "password=value", "password: value", "\"password\": \"value\"", "Authorization: Bearer value".
                // Quoted values are masked up to the closing quote, so values with spaces are masked completely.
                _patterns.Add((new Regex(
                    $@"(?<prefix>{escaped}[""']?\s*[:=]\s*)(?:(?<quote>[""'])(?:\\.|(?!\k<quote>)[^\\\r\n])*\k<quote>|(?<scheme>(?:{AuthSchemes})\s+)?\S+)",
                    PatternOptions,
                    matchTimeout), MaskSeparatedValue));

                // "password somevalue" (value must be 4+ chars to avoid natural language)
                _patterns.Add((new Regex(
                    $@"(?<prefix>{escaped}\s+[""']?(?:(?:{AuthSchemes})\s+)?)\S{{4,}}",
                    PatternOptions,
                    matchTimeout), MaskSpacedValue));
            }
        }

        public bool Contains(string normalizedKey) => _normalizedKeys.Contains(normalizedKey);

        public bool IsBuiltFrom(IReadOnlyCollection<string> keys)
            => ReferenceEquals(_source, keys) || (_source is not null && _sourceKeys.SetEquals(keys));

        public string MaskPatterns(string message)
        {
            try
            {
                var result = message;
                foreach (var (pattern, mask) in _patterns)
                    result = pattern.Replace(result, mask);

                return result;
            }
            catch (RegexMatchTimeoutException)
            {
                // Masking took too long (a huge message). Hide all of it rather than leak a value or throw from a log call.
                return _placeholder;
            }
        }

        public string MaskLiterals(string message)
        {
            var result = message;
            foreach (var literal in _literals)
                result = result.Replace(literal, _placeholder, StringComparison.OrdinalIgnoreCase);

            return result;
        }

        private string MaskSeparatedValue(Match match)
        {
            var quote = match.Groups["quote"];
            return quote.Success
                ? match.Groups["prefix"].Value + quote.Value + _placeholder + quote.Value
                : match.Groups["prefix"].Value + match.Groups["scheme"].Value + _placeholder;
        }

        private string MaskSpacedValue(Match match) => match.Groups["prefix"].Value + _placeholder;
    }
}
