namespace Tjidde.Logging.Context;

/// <summary>
/// Provides access to the current customer/tenant context for log enrichment.
/// Implement this interface and register it in DI to supply a customer prefix
/// that will be included in every log entry.
/// </summary>
public interface ICustomerContextAccessor
{
    /// <summary>
    /// Returns the current customer identifier or prefix, or null if no customer context is active.
    /// </summary>
    string? GetCustomerContext();
}
