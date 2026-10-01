using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.Extensions;

/// <summary>
/// Extension methods for recording the calling method name in log output.
/// </summary>
public static class MethodScopeLoggerExtensions
{
    /// <summary>
    /// Begins a scope that records the calling method name, which the Tjidde logger shows as
    /// <c>Method=&gt;Name</c>. The name is filled in by the compiler, so this has no runtime cost.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to begin the scope on.</param>
    /// <param name="methodName">The method name. Supplied automatically by the compiler; do not pass it.</param>
    /// <returns>A disposable that ends the scope.</returns>
    public static IDisposable? BeginMethodScope(this ILogger logger, [CallerMemberName] string methodName = "")
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.BeginScope(new[] { new KeyValuePair<string, object?>("MethodName", methodName) });
    }
}
