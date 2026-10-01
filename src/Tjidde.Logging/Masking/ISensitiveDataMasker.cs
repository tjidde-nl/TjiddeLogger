namespace Tjidde.Logging.Masking;

/// <summary>
/// Defines the contract for masking sensitive data in log messages and structured log state.
/// </summary>
public interface ISensitiveDataMasker
{
    /// <summary>
    /// Returns true if the given key is considered sensitive and its value should be masked.
    /// </summary>
    bool IsSensitiveKey(string key);

    /// <summary>
    /// Scans a plain log message string and replaces known sensitive patterns with the mask placeholder.
    /// </summary>
    string MaskMessage(string message);

    /// <summary>
    /// Returns the masked value for a given key/value pair.
    /// If the key is sensitive, returns the placeholder; otherwise returns the original value.
    /// </summary>
    string MaskValue(string key, string value);

    /// <summary>
    /// Masks all sensitive values in a collection of structured log properties.
    /// Returns a new dictionary with safe string representations.
    /// </summary>
    IReadOnlyDictionary<string, string> MaskDictionary(IEnumerable<KeyValuePair<string, object?>> properties);
}
