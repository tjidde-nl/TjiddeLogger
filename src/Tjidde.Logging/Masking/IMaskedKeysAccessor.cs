namespace Tjidde.Logging.Masking;

/// <summary>
/// Provides access to extra sensitive keys managed at runtime.
/// </summary>
public interface IMaskedKeysAccessor
{
    /// <summary>
    /// Gets the current set of extra sensitive keys.
    /// </summary>
    IReadOnlyCollection<string> GetKeys();
}

/// <summary>
/// Default implementation that retrieves keys from <see cref="MaskedKeysContext"/>.
/// </summary>
public sealed class GlobalMaskedKeysAccessor : IMaskedKeysAccessor
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> GetKeys() => MaskedKeysContext.GetKeys();
}
