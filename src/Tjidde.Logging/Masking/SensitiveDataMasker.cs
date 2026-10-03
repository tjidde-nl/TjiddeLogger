#if NET9_0_OR_GREATER
using System.Buffers;
#endif
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
    // True when the accessor hands out immutable snapshots (MaskedKeysStore, MaskedKeysContext), so an unchanged reference
    // means unchanged keys. Any other accessor may return a collection it changes later, so it is compared by content.
    private readonly bool _runtimeKeysAreSnapshots;
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
        _runtimeKeysAreSnapshots = runtimeKeysAccessor is GlobalMaskedKeysAccessor or MaskedKeysStore;

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
        if (current is not null && current.IsBuiltFrom(keys, _runtimeKeysAreSnapshots))
            return current;

        // The keys changed since the last call. Build from a copy: a custom accessor may return a collection it
        // changes later. The masker is shared by all loggers, so other threads may rebuild at the same time; each
        // call masks with the set built from the keys it read itself. The cache is only replaced when no other thread
        // replaced it in the meantime; a set that is out of date is rebuilt by the next call that sees other keys.
        var snapshot = keys.ToArray();
        var rebuilt = new KeySet(snapshot, snapshot, _placeholder, _matchTimeout, source: snapshot, sourceReference: keys);
        Interlocked.CompareExchange(ref _runtimeKeys, rebuilt, current);
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

        // Not RegexOptions.Compiled: the patterns are rebuilt whenever the runtime keys or the options change, and the JIT time of a compiled
        // pattern's first match counts toward the match timeout, which made busy apps hit timeouts.
        private const RegexOptions PatternOptions = RegexOptions.IgnoreCase;

        private readonly HashSet<string> _normalizedKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyPatterns> _patterns = [];
#if NET9_0_OR_GREATER
        // All ASCII keys at once: one vectorized scan tells whether a message can match any of their patterns.
        private readonly SearchValues<string>? _asciiKeySearch;
#endif
        private readonly bool _hasNonAsciiKey;
        private readonly string[] _literals;
        private readonly string _placeholder;
        private readonly MatchEvaluator _maskSeparatedValue;
        private readonly MatchEvaluator _maskSpacedValue;
        private readonly object? _sourceReference;
        private readonly bool _hasSource;
        private readonly HashSet<string> _sourceKeys;

        public KeySet(
            IEnumerable<string> keys,
            IEnumerable<string> literals,
            string placeholder,
            TimeSpan matchTimeout,
            IReadOnlyCollection<string>? source = null,
            object? sourceReference = null)
        {
            _placeholder = placeholder;
            _maskSeparatedValue = MaskSeparatedValue;
            _maskSpacedValue = MaskSpacedValue;
            _literals = literals.Where(literal => !string.IsNullOrWhiteSpace(literal)).ToArray();
            _sourceReference = sourceReference;
            _hasSource = source is not null;
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
                var separated = new Regex(
                    $@"(?<prefix>{escaped}[""']?\s*[:=]\s*)(?:(?<quote>[""'])(?:\\.|(?!\k<quote>)[^\\\r\n])*\k<quote>|(?<scheme>(?:{AuthSchemes})\s+)?\S+)",
                    PatternOptions,
                    matchTimeout);

                // "password somevalue" (value must be 4+ chars to avoid natural language)
                var spaced = new Regex(
                    $@"(?<prefix>{escaped}\s+[""']?(?:(?:{AuthSchemes})\s+)?)\S{{4,}}",
                    PatternOptions,
                    matchTimeout);

                var isAscii = IsAscii(key);
                _hasNonAsciiKey |= !isAscii;
                _patterns.Add(new KeyPatterns(key, isAscii, separated, spaced));
            }

#if NET9_0_OR_GREATER
            var asciiKeys = _patterns.Where(p => p.KeyIsAscii).Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (asciiKeys.Length > 0)
                _asciiKeySearch = SearchValues.Create(asciiKeys, StringComparison.OrdinalIgnoreCase);
#endif
        }

        /// <summary>The patterns of one key; every match of either pattern contains the key itself.</summary>
        private sealed record KeyPatterns(string Key, bool KeyIsAscii, Regex Separated, Regex Spaced);

        private static bool IsAscii(string text)
        {
#if NET8_0_OR_GREATER
            return System.Text.Ascii.IsValid(text);
#else
            foreach (var c in text)
            {
                if (c > '\u007F')
                    return false;
            }

            return true;
#endif
        }

        public bool Contains(string normalizedKey) => _normalizedKeys.Contains(normalizedKey);

        /// <summary>
        /// Whether this set was built from the same keys. The reference check is only trusted for immutable
        /// snapshots; any other collection is compared by content.
        /// </summary>
        public bool IsBuiltFrom(IReadOnlyCollection<string> keys, bool referenceMeansUnchanged)
            => _hasSource
               && ((referenceMeansUnchanged && ReferenceEquals(_sourceReference, keys)) || _sourceKeys.SetEquals(keys));

        public string MaskPatterns(string message)
        {
            if (_patterns.Count == 0)
                return message;

            try
            {
                var result = message;

                // Every match contains its key, so a key that does not occur in the message cannot match and its
                // patterns are skipped. The ordinal check only rules a key out for ASCII text and an ASCII key: there it
                // agrees with the patterns' IgnoreCase matching, while non-ASCII text can match case-insensitively in
                // ways an ordinal comparison does not (for example the Kelvin sign and "k"); those always run the patterns.
                var resultIsAscii = IsAscii(result);
#if NET9_0_OR_GREATER
                if (resultIsAscii && !_hasNonAsciiKey && (_asciiKeySearch is null || !result.AsSpan().ContainsAny(_asciiKeySearch)))
                    return result;
#endif

                foreach (var key in _patterns)
                {
                    result = Replace(key.Separated, result, _maskSeparatedValue, key, ref resultIsAscii);
                    result = Replace(key.Spaced, result, _maskSpacedValue, key, ref resultIsAscii);
                }

                return result;
            }
            catch (RegexMatchTimeoutException)
            {
                // Masking took too long (a huge message). Hide all of it rather than leak a value or throw from a log call.
                return _placeholder;
            }
        }

        private static string Replace(Regex pattern, string text, MatchEvaluator mask, KeyPatterns key, ref bool textIsAscii)
        {
            if (textIsAscii && key.KeyIsAscii && !text.Contains(key.Key, StringComparison.OrdinalIgnoreCase))
                return text;

            var replaced = pattern.Replace(text, mask);

            // Regex.Replace returns the same instance when nothing matched; the placeholder may be non-ASCII.
            if (!ReferenceEquals(replaced, text))
                textIsAscii = IsAscii(replaced);

            return replaced;
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
