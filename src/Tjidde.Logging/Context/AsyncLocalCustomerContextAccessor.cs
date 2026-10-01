namespace Tjidde.Logging.Context;

/// <summary>
/// Default implementation of <see cref="ICustomerContextAccessor"/> that reads
/// the customer context from the ambient <see cref="CustomerContext"/> AsyncLocal store.
/// </summary>
public sealed class AsyncLocalCustomerContextAccessor : ICustomerContextAccessor
{
    /// <inheritdoc />
    public string? GetCustomerContext() => CustomerContext.Current;
}
