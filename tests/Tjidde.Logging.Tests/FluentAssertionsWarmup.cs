using System.Runtime.CompilerServices;
using FluentAssertions;

namespace Tjidde.Logging.Tests;

internal static class FluentAssertionsWarmup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Trigger FluentAssertions license warning before any console-capturing tests run.
        // We intentionally do NOT dispose the StringWriter to avoid race conditions
        // where Console.Out might still reference it.
        var original = Console.Out;
        var sink = new StringWriter();
        Console.SetOut(sink);
        try
        {
            true.Should().BeTrue();
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
