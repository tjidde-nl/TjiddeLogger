using FluentAssertions;
using Tjidde.Logging.Context;
using Tjidde.Logging.Formatting;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Tjidde.Logging.Tests;

[Collection("ConsoleOutput")]
public sealed class DynamicCensoringTests
{
    private static readonly Tjidde.Logging.Logging.ConsoleLogProcessor Processor = new();

    private static Tjidde.Logging.Logging.TjiddeLogger BuildLogger(
        string category = "My.Namespace.MyService",
        TjiddeLoggerOptions? options = null,
        IEnumerable<string>? dynamicKeys = null)
    {
        var opts = options ?? new TjiddeLoggerOptions();
        var acc = new AsyncLocalCustomerContextAccessor();
        var masker = new SensitiveDataMasker(opts.MaskPlaceholder, opts.AdditionalSensitiveKeys, dynamicKeys);
        var formatter = new ExceptionFormatter(opts.IncludeStackTrace, opts.IncludeInnerExceptions);

        return new Tjidde.Logging.Logging.TjiddeLogger(
            category, opts, acc, masker, formatter, Processor, scopeProvider: null);
    }

    private static string CaptureConsoleOutput(Action action)
    {
        var original = Console.Out;
        var writer = new System.IO.StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
            Processor.Flush(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    [Fact]
    public void Log_MasksDynamicKey_WhenAddedAtRuntime()
    {
        // Arrange
        CustomerContext.Clear();
        const string secretWord = "SuperSecret42";
        const string message = $"The code is {secretWord}";

        // Act - Before adding dynamic key: logger without dynamic keys
        var loggerBefore = BuildLogger();
        var outputBefore = CaptureConsoleOutput(() =>
            loggerBefore.Log(LogLevel.Information, new EventId(0), message,
                null, (s, _) => s));

        // Act - After adding dynamic key: logger with dynamic keys
        var loggerAfter = BuildLogger(dynamicKeys: [secretWord]);
        var outputAfter = CaptureConsoleOutput(() =>
            loggerAfter.Log(LogLevel.Information, new EventId(0), message,
                null, (s, _) => s));

        // Assert
        outputBefore.Should().Contain(secretWord);
        outputAfter.Should().NotContain(secretWord);
        outputAfter.Should().Contain("[REDACTED]");
    }
}
