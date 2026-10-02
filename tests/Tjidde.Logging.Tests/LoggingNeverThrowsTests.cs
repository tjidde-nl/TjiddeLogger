using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Context;
using Tjidde.Logging.Formatting;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Xunit;

namespace Tjidde.Logging.Tests;

/// <summary>
/// Logging must never throw: values that cannot be serialized or formatted only affect themselves,
/// and anything else produces a minimal fallback line.
/// </summary>
[Collection("ConsoleOutput")]
public sealed class LoggingNeverThrowsTests
{
    private static readonly ConsoleLogProcessor Processor = new();

    private static TjiddeLogger BuildLogger(
        TjiddeLoggerOptions? options = null,
        IExceptionFormatter? exceptionFormatter = null,
        ICustomerContextAccessor? accessor = null)
    {
        var opts = options ?? new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json };
        var masker = new SensitiveDataMasker(opts.MaskPlaceholder, opts.AdditionalSensitiveKeys);
        return new TjiddeLogger(
            "My.Namespace.MyService",
            opts,
            accessor ?? new AsyncLocalCustomerContextAccessor(),
            masker,
            exceptionFormatter ?? new ExceptionFormatter(opts.IncludeStackTrace, opts.IncludeInnerExceptions),
            Processor,
            scopeProvider: null);
    }

    private static string Capture(Action log)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            log.Should().NotThrow();
            Processor.Flush(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString().Trim();
    }

    private static JsonElement CaptureJson(Action log)
    {
        var output = Capture(log);
        output.Split(Environment.NewLine).Should().ContainSingle("one log entry is one line");
        using var doc = JsonDocument.Parse(output);
        return doc.RootElement.Clone();
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> State(params (string Key, object? Value)[] pairs)
        => pairs.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)).ToList();

    [Fact]
    public void Json_ObjectWithCycle_IsWrittenWithoutTheCycle()
    {
        var node = new Node { Name = "a" };
        node.Next = node;
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.LogInformation("Node {Node}", node));

        var written = root.GetProperty("properties").GetProperty("Node");
        written.GetProperty("name").GetString().Should().Be("a");
        written.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Json_NullValues_AreWrittenAsNull()
    {
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.LogInformation("User {User} with {Password}", null, null));

        root.GetProperty("message").GetString().Should().Contain("User (null)");
        var properties = root.GetProperty("properties");
        properties.GetProperty("User").ValueKind.Should().Be(JsonValueKind.Null);
        properties.GetProperty("Password").GetString().Should().Be("[REDACTED]");
    }

    [Fact]
    public void Json_Collections_AreWrittenAsArraysAndObjects()
    {
        var logger = BuildLogger();
        var ids = new List<int> { 1, 2, 3 };
        var totals = new Dictionary<string, int> { ["eu"] = 5 };

        var root = CaptureJson(() => logger.LogInformation("Ids {Ids} totals {Totals}", ids, totals));

        root.GetProperty("message").GetString().Should().Be("Ids 1, 2, 3 totals [eu, 5]");
        var properties = root.GetProperty("properties");
        properties.GetProperty("Ids").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1, 2, 3);
        properties.GetProperty("Totals").GetProperty("eu").GetInt32().Should().Be(5);
    }

    [Fact]
    public void Json_ValueWhoseToStringThrows_IsSerializedAndMessageFallsBackToTemplate()
    {
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.LogInformation("Order {Order}", new ThrowingToString { Id = 7 }));

        root.GetProperty("level").GetString().Should().Be("INFORMATION");
        root.GetProperty("message").GetString().Should().StartWith("Order {Order}");
        root.GetProperty("properties").GetProperty("Order").GetProperty("id").GetInt32().Should().Be(7);
    }

    [Fact]
    public void Json_SensitiveValueWhoseToStringThrows_IsMarkedUnserializable()
    {
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.Log(LogLevel.Information, new EventId(0),
            State(("Password", new ThrowingToString { Id = 7 })), null, (_, _) => "Login"));

        root.GetProperty("properties").GetProperty("Password").GetString()
            .Should().Be($"[unserializable: {typeof(ThrowingToString).FullName}]");
    }

    [Fact]
    public void Json_ValueWithThrowingGetter_FallsBackToToString()
    {
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.LogInformation("Value {Value}", new ThrowingGetter()));

        root.GetProperty("message").GetString().Should().Be("Value throwing-getter");
        root.GetProperty("properties").GetProperty("Value").GetString().Should().Be("throwing-getter");
    }

    [Fact]
    public void Json_ValueWithThrowingGetterAndToString_IsMarkedUnserializable()
    {
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.Log(LogLevel.Information, new EventId(0),
            State(("Value", new ThrowingEverything())), null, (_, _) => "Something happened"));

        root.GetProperty("message").GetString().Should().Be("Something happened");
        root.GetProperty("properties").GetProperty("Value").GetString()
            .Should().Be($"[unserializable: {typeof(ThrowingEverything).FullName}]");
    }

    [Fact]
    public void Json_UnsupportedType_FallsBackToToString()
    {
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.LogInformation("Type {Type}", typeof(string)));

        root.GetProperty("properties").GetProperty("Type").GetString().Should().Be("System.String");
    }

    [Fact]
    public void Json_EnumerableThatThrows_DoesNotThrow()
    {
        var logger = BuildLogger();

        var root = CaptureJson(() => logger.LogInformation("Items {Items} for {Password}", ThrowingSequence(), "hunter2"));

        root.GetProperty("level").GetString().Should().Be("INFORMATION");
        root.GetRawText().Should().NotContain("hunter2");
    }

    [Fact]
    public void Json_ThrowingExceptionFormatter_FallsBackToTypeAndMessage()
    {
        var logger = BuildLogger(exceptionFormatter: new ThrowingExceptionFormatter());

        var root = CaptureJson(() => logger.LogError(new InvalidOperationException("Boom"), "Failed"));

        root.GetProperty("message").GetString().Should().Be("Failed");
        var exception = root.GetProperty("exception").GetString();
        exception.Should().Contain("System.InvalidOperationException: Boom");
        exception.Should().Contain("exception formatter failed");
    }

    [Fact]
    public void Text_ThrowingExceptionFormatter_FallsBackToTypeAndMessage()
    {
        var logger = BuildLogger(new TjiddeLoggerOptions(), new ThrowingExceptionFormatter());

        var output = Capture(() => logger.LogError(new InvalidOperationException("Boom"), "Failed"));

        output.Should().Contain("[ERROR]");
        output.Should().Contain("Failed | Exception: System.InvalidOperationException: Boom");
    }

    [Fact]
    public void Text_ValueWhoseToStringThrows_DoesNotThrow()
    {
        var logger = BuildLogger(new TjiddeLoggerOptions());

        var output = Capture(() => logger.LogInformation("Order {Order}", new ThrowingToString()));

        output.Should().Contain("[INFORMATION]");
        output.Should().Contain("Order {Order}");
    }

    [Fact]
    public void ThrowingCustomerAccessor_WritesMinimalFallbackLine()
    {
        var logger = BuildLogger(accessor: new ThrowingAccessor());

        var root = CaptureJson(() => logger.LogWarning("Payment {Amount} by {Password}", 12, "hunter2"));

        root.GetProperty("level").GetString().Should().Be("WARNING");
        root.GetProperty("category").GetString().Should().Be("My.Namespace.MyService");
        root.GetProperty("message").GetString().Should().Be("Payment {Amount} by {Password}");
        root.GetProperty("renderError").GetString().Should().Contain("InvalidOperationException");
        root.GetRawText().Should().NotContain("hunter2");
    }

    [Fact]
    public void Text_ThrowingCustomerAccessor_WritesMinimalFallbackLine()
    {
        var logger = BuildLogger(new TjiddeLoggerOptions(), accessor: new ThrowingAccessor());

        var output = Capture(() => logger.LogWarning("Disk almost full"));

        output.Should().Contain("[WARNING] Class=>MyService: Disk almost full");
        output.Should().Contain("could not be rendered");
        output.Should().Contain("My.Namespace.MyService");
    }

    [Fact]
    public void OpenTelemetryExport_WithExceptionWhoseMessageThrows_DoesNotThrow()
    {
        var logger = BuildLogger(new TjiddeLoggerOptions { EnableOpenTelemetryExport = true });
        using var activity = new Activity("request").Start();

        var output = Capture(() => logger.LogError(new ThrowingMessageException(), "Failed"));

        output.Should().Contain("Failed");
        activity.Events.Should().ContainSingle();
    }

    private sealed class Node
    {
        public string Name { get; set; } = string.Empty;
        public Node? Next { get; set; }
    }

    private sealed class ThrowingToString
    {
        public int Id { get; set; }
        public override string ToString() => throw new InvalidOperationException("ToString failed");
    }

    private sealed class ThrowingGetter
    {
        public int Value => throw new InvalidOperationException("getter failed");
        public override string ToString() => "throwing-getter";
    }

    private sealed class ThrowingEverything
    {
        public int Value => throw new InvalidOperationException("getter failed");
        public override string ToString() => throw new InvalidOperationException("ToString failed");
    }

    private static IEnumerable<int> ThrowingSequence()
    {
        yield return 1;
        throw new InvalidOperationException("enumeration failed");
    }

    private sealed class ThrowingExceptionFormatter : IExceptionFormatter
    {
        public string Format(Exception exception) => throw new NotSupportedException("formatter failed");
    }

    private sealed class ThrowingAccessor : ICustomerContextAccessor
    {
        public string? GetCustomerContext() => throw new InvalidOperationException("no context");
    }

    private sealed class ThrowingMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("message failed");
    }
}
