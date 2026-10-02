using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Formatting;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.Tests;

/// <summary>
/// Tests the rendered log line format by invoking TjiddeLogger directly
/// and capturing Console output.
/// </summary>
[Collection("ConsoleOutput")]
public sealed class LogOutputFormatTests
{
    // Matches: YYYY-MM-DD: HH:mm:ss: [LVL] CS=>ClassName Method=>MethodName: Client=>CustomerName: Message
    private static readonly Regex LogLinePattern = new(
        @"^\d{4}-\d{2}-\d{2}: \d{2}:\d{2}:\d{2}: \[.+\] Class=>.+: .+$",
        RegexOptions.Compiled);

    private static readonly Tjidde.Logging.Logging.ConsoleLogProcessor Processor = new();

    private static Tjidde.Logging.Logging.TjiddeLogger BuildLogger(
        string category = "My.Namespace.MyService",
        TjiddeLoggerOptions? options = null,
        ICustomerContextAccessor? accessor = null,
        IExternalScopeProvider? scopeProvider = null)
    {
        var opts = options ?? new TjiddeLoggerOptions();
        var acc = accessor ?? new AsyncLocalCustomerContextAccessor();
        var masker = new SensitiveDataMasker(opts.MaskPlaceholder, opts.AdditionalSensitiveKeys);
        var formatter = new ExceptionFormatter(opts.IncludeStackTrace, opts.IncludeInnerExceptions);

        return new Tjidde.Logging.Logging.TjiddeLogger(
            category, opts, acc, masker, formatter, Processor, scopeProvider);
    }

    private static string CaptureConsoleOutput(Action action)
    {
        var original = Console.Out;
        using var writer = new System.IO.StringWriter();
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
    public void Log_OutputMatchesExpectedFormat()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Hello World",
                null, (s, _) => s));

        output.Trim().Should().MatchRegex(LogLinePattern);
    }

    [Fact]
    public void Log_OutputContainsClassName()
    {
        CustomerContext.Clear();
        var logger = BuildLogger("My.Namespace.MyService");

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Test message",
                null, (s, _) => s));

        output.Should().Contain("MyService");
    }

    [Fact]
    public void Log_OutputContainsCallingMethodName_WhenStackTraceResolutionIsEnabled()
    {
        CustomerContext.Clear();
        var logger = BuildLogger("My.Namespace.MyService",
            new TjiddeLoggerOptions { ResolveMethodNameFromStackTrace = true });

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Test message",
                null, (s, _) => s));

        output.Should().Contain($"Method=>{nameof(CaptureConsoleOutput)}");
    }

    [Fact]
    public void Log_OmitsMethodName_WhenNoMethodScopeAndStackTraceResolutionIsDisabled()
    {
        CustomerContext.Clear();
        var logger = BuildLogger("My.Namespace.MyService");

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Test message",
                null, (s, _) => s));

        output.Should().NotContain("Method=>");
    }

    [Fact]
    public void Log_BeginMethodScope_AddsCallerMethodName()
    {
        CustomerContext.Clear();
        var logger = BuildLogger(scopeProvider: new LoggerExternalScopeProvider());

        var output = CaptureConsoleOutput(() =>
        {
            using (logger.BeginMethodScope())
                logger.Log(LogLevel.Information, new EventId(0), "Test message", null, (s, _) => s);
        });

        output.Should().Contain($"Method=>{nameof(Log_BeginMethodScope_AddsCallerMethodName)}:");
        output.Should().NotContain("MethodName=");
    }

    [Fact]
    public void Log_OutputContainsCustomerContext()
    {
        CustomerContext.Set("AcmeCorp");
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Order placed",
                null, (s, _) => s));

        CustomerContext.Clear();

        output.Should().Contain("AcmeCorp");
    }

    [Fact]
    public void Log_OutputDoesNotContainCustomer_WhenContextIsNull()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "No customer here",
                null, (s, _) => s));

        // Should still match the format without a customer segment
        output.Should().Contain("No customer here");
        output.Should().MatchRegex(LogLinePattern);
    }

    [Fact]
    public void Log_OutputContainsLogLevel()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Warning, new EventId(0), "Watch out",
                null, (s, _) => s));

        output.Should().Contain("[WARNING]");
    }

    [Fact]
    public void Log_MasksPasswordInMessage()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Login with password=hunter2",
                null, (s, _) => s));

        output.Should().Contain("[REDACTED]");
        output.Should().NotContain("hunter2");
    }

    [Fact]
    public void Log_ExceptionDetailsAppendedToOutput()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();
        var ex = new InvalidOperationException("Boom");

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Error, new EventId(0), "Something failed",
                ex, (s, _) => s));

        output.Should().Contain("System.InvalidOperationException");
        output.Should().Contain("Boom");
    }

    [Fact]
    public void Log_ContainsDateInCorrectFormat()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Date check",
                null, (s, _) => s));

        output.Should().Contain(today);
    }

    [Fact]
    public void Log_MaskingDisabled_DoesNotMaskMessage()
    {
        CustomerContext.Clear();
        var options = new TjiddeLoggerOptions { EnableSensitiveDataMasking = false };
        var logger = BuildLogger(options: options);

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "Login with password=hunter2",
                null, (s, _) => s));

        output.Should().Contain("hunter2");
    }

    [Fact]
    public void Log_ObsoleteMetricsLevel_OutputContainsMetricsLabel()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
#pragma warning disable CS0618 // (LogLevel)10 must keep working for existing callers
            logger.Log(MetricsLoggerExtensions.Metrics, new EventId(0), "requests_total=42",
                null, (s, _) => s));
#pragma warning restore CS0618

        output.Should().Contain("[METRICS]");
        output.Should().Contain("requests_total=42");
        output.Trim().Should().MatchRegex(LogLinePattern);
    }

    [Fact]
    public void LogMetrics_OutputContainsMetricsLabel()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() => logger.LogMetrics("requests_total={Count}", 42));

        output.Should().Contain("[METRICS]");
        output.Should().Contain("requests_total=42");
        output.Trim().Should().MatchRegex(LogLinePattern);
    }

#pragma warning disable CS0618 // Obsolete MinimumLevel/CategoryMinimumLevels: verifies the legacy filter still works until 2.0
    [Fact]
    public void LogMetrics_IsNotDroppedByMinimumLevel()
    {
        CustomerContext.Clear();
        var logger = BuildLogger(options: new TjiddeLoggerOptions { MinimumLevel = LogLevel.Critical });

        var output = CaptureConsoleOutput(() =>
        {
            logger.LogInformation("plain information");
            logger.LogMetrics("requests_total={Count}", 42);
        });

        output.Should().NotContain("plain information");
        output.Should().Contain("[METRICS]");
    }
#pragma warning restore CS0618

    [Fact]
    public void LogMetrics_JsonOutput_UsesMetricsLevelAndEvent()
    {
        CustomerContext.Clear();
        var logger = BuildLogger(options: new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json });

        var output = CaptureConsoleOutput(() => logger.LogMetrics("requests_total={Count}", 42));

        using var doc = JsonDocument.Parse(output.Trim());
        doc.RootElement.GetProperty("level").GetString().Should().Be("METRICS");
        doc.RootElement.GetProperty("eventName").GetString().Should().Be("Metrics");
        doc.RootElement.GetProperty("eventId").GetInt32().Should().Be(MetricsLoggerExtensions.MetricsEventIdValue);
    }

    [Fact]
    public void Log_JsonOutput_ContainsExpectedFields()
    {
        CustomerContext.Set("AcmeCorp");
        var options = new TjiddeLoggerOptions
        {
            OutputFormat = TjiddeLogOutputFormat.Json,
            ResolveMethodNameFromStackTrace = true
        };
        var logger = BuildLogger("My.Namespace.MyService", options: options);

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(42, "OrderCreated"), "Order placed",
                null, (s, _) => s));

        CustomerContext.Clear();
        using var doc = JsonDocument.Parse(output.Trim());
        var root = doc.RootElement;

        root.GetProperty("message").GetString().Should().Be("Order placed");
        root.GetProperty("level").GetString().Should().Be("INFORMATION");
        root.GetProperty("category").GetString().Should().Be("My.Namespace.MyService");
        root.GetProperty("eventId").GetInt32().Should().Be(42);
        root.GetProperty("customer").GetString().Should().Be("AcmeCorp");
        root.GetProperty("method").GetString().Should().Be(nameof(CaptureConsoleOutput));
        root.TryGetProperty("@timestamp", out _).Should().BeTrue();
    }

#pragma warning disable CS0618 // Obsolete MinimumLevel/CategoryMinimumLevels: verifies the legacy filter still works until 2.0
    [Fact]
    public void IsEnabled_UsesCategoryMinimumLevels_WithNamespaceFallback()
    {
        var options = new TjiddeLoggerOptions
        {
            MinimumLevel = LogLevel.Trace,
            CategoryMinimumLevels = new Dictionary<string, LogLevel>
            {
                ["Default"] = LogLevel.Warning,
                ["My.Namespace"] = LogLevel.Error
            }
        };

        var logger = BuildLogger("My.Namespace.MyService", options);

        logger.IsEnabled(LogLevel.Warning).Should().BeFalse();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
    }
#pragma warning restore CS0618

    [Fact]
    public void Log_OpenTelemetryExportEnabled_AddsEventToCurrentActivity()
    {
        CustomerContext.Clear();
        var options = new TjiddeLoggerOptions
        {
            EnableOpenTelemetryExport = true
        };
        var logger = BuildLogger("My.Namespace.MyService", options);

        using var activity = new Activity("request").Start();

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(15, "Created"), "Order placed",
                null, (s, _) => s));

        output.Should().Contain("Order placed");

        activity.Events.Should().ContainSingle();
        var logEvent = activity.Events.Single();
        logEvent.Name.Should().Be("log");
        logEvent.Tags.Should().Contain(t => t.Key == "log.category" && (string?)t.Value == "My.Namespace.MyService");
        logEvent.Tags.Should().Contain(t => t.Key == "log.level" && (string?)t.Value == "INFORMATION");
        logEvent.Tags.Should().Contain(t => t.Key == "log.message" && (string?)t.Value == "Order placed");
        logEvent.Tags.Should().Contain(t => t.Key == "event.id" && (int?)t.Value == 15);
        logEvent.Tags.Should().Contain(t => t.Key == "event.name" && (string?)t.Value == "Created");
    }

    [Fact]
    public void Log_OpenTelemetryExportDisabled_DoesNotAddEventToCurrentActivity()
    {
        CustomerContext.Clear();
        var logger = BuildLogger("My.Namespace.MyService", new TjiddeLoggerOptions
        {
            EnableOpenTelemetryExport = false
        });

        using var activity = new Activity("request").Start();

        CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0), "No export",
                null, (s, _) => s));

        activity.Events.Should().BeEmpty();
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> State(params (string Key, object? Value)[] pairs)
        => pairs.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)).ToList();

    [Fact]
    public void Log_JsonProperties_MaskSensitiveValues()
    {
        var logger = BuildLogger(options: new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json });

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0),
                State(("{OriginalFormat}", "Login {User}"), ("User", "bob"), ("Password", "hunter2")),
                null, (_, _) => "Login bob"));

        output.Should().NotContain("hunter2");
        output.Should().Contain("[REDACTED]");
        output.Should().Contain("bob");
    }

    [Fact]
    public void Log_JsonProperties_MaskSensitiveLiteralsInNonSensitiveKeys()
    {
        var logger = BuildLogger(options: new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json });

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Information, new EventId(0),
                State(("Query", "user=bob&password=hunter2")),
                null, (_, _) => "Query received"));

        output.Should().NotContain("hunter2");
    }

    [Fact]
    public void Log_ExceptionMessage_IsMasked()
    {
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Error, new EventId(0), "Failed",
                new InvalidOperationException("token=abc123secret"), (s, _) => s));

        output.Should().NotContain("abc123secret");
    }

    [Fact]
    public void Log_MessageTemplate_MasksSensitiveValueInRenderedMessage()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.LogInformation("Login {User} with {Password}", "bob", "hunter2"));

        output.Should().Contain("Login bob with [REDACTED]");
        output.Should().NotContain("hunter2");
    }

    [Fact]
    public void Log_MessageTemplate_KeepsFrameworkFormatting_WhenMasking()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            logger.LogInformation("Paid {Amount:0.00} for {Items} {{literal}} with {Password}",
                12.5, new[] { 1, 2 }, "hunter2"));

        output.Should().Contain("Paid 12.50 for 1, 2 {literal} with [REDACTED]");
    }

    private static readonly Action<ILogger, string, string, Exception?> LogLogin =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1), "Login {User} with {Password}");

    [Fact]
    public void Log_LoggerMessageDefine_MasksSensitiveValueInRenderedMessage()
    {
        CustomerContext.Clear();
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() => LogLogin(logger, "bob", "hunter2", null));

        output.Should().Contain("Login bob with [REDACTED]");
        output.Should().NotContain("hunter2");
    }

    [Fact]
    public void Log_OpenTelemetryExport_MasksSensitiveValues()
    {
        CustomerContext.Clear();
        var logger = BuildLogger(options: new TjiddeLoggerOptions { EnableOpenTelemetryExport = true });

        using var activity = new Activity("request").Start();

        CaptureConsoleOutput(() =>
            logger.Log(LogLevel.Error, new EventId(0),
                State(("User", "bob"), ("Password", "hunter2"), ("Query", "password=hunter2")),
                new InvalidOperationException("token=abc123secret"), (_, _) => "Login failed"));

        var tags = activity.Events.Should().ContainSingle().Subject.Tags.ToList();
        tags.Should().Contain(t => t.Key == "log.property.Password" && (string?)t.Value == "[REDACTED]");
        tags.Should().Contain(t => t.Key == "log.property.User" && (string?)t.Value == "bob");
        tags.Should().Contain(t => t.Key == "log.masked_fields" && (string?)t.Value == "Password");
        tags.Should().NotContain(t => t.Value != null && t.Value.ToString()!.Contains("hunter2"));
        tags.Should().NotContain(t => t.Value != null && t.Value.ToString()!.Contains("abc123secret"));
    }

    [Fact]
    public void Log_ConcurrentWrites_DoNotInterleaveLines()
    {
        var logger = BuildLogger();

        var output = CaptureConsoleOutput(() =>
            Parallel.For(0, 200, i =>
                logger.Log(LogLevel.Information, new EventId(0), $"Message {i}", null, (s, _) => s)));

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(200);
        lines.Should().OnlyContain(l => LogLinePattern.IsMatch(l));
    }
}
