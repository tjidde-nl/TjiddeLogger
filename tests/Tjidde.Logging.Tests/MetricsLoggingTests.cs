using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Extensions;
using Xunit;

namespace Tjidde.Logging.Tests;

/// <summary>
/// LogMetrics must work next to other logging providers (e.g. the Microsoft console logger),
/// which reject unknown <see cref="LogLevel"/> values.
/// </summary>
[Collection("ConsoleOutput")]
public sealed class MetricsLoggingTests
{
    private static string CaptureConsoleOutput(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("json")]
    [InlineData("systemd")]
    public void LogMetrics_WithMicrosoftConsoleLoggerRegistered_DoesNotThrow(string formatterName)
    {
        var output = CaptureConsoleOutput(() =>
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddTjiddeLogger()
                .AddConsole(o => o.FormatterName = formatterName));

            // Disposing the provider flushes both the Tjidde and the Microsoft console queues.
            using var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILogger<MetricsLoggingTests>>();

            var act = () =>
            {
                logger.LogMetrics("requests_total={Count}", 42);
                logger.LogMetrics(new InvalidOperationException("boom"), "failures_total={Count}", 1);
                logger.LogMetrics(new EventId(200, "Throughput"), "throughput_rps={Rps}", 12);
            };

            act.Should().NotThrow();
        });

        output.Should().Contain("[METRICS]");
        output.Should().Contain("requests_total=42");
    }

    [Fact]
    public void LogMetrics_UsesInformationLevelWithMetricsEventId()
    {
        var logger = new RecordingLogger();

        logger.LogMetrics("requests_total={Count}", 42);
        logger.LogMetrics(new InvalidOperationException("boom"), "failures_total={Count}", 1);

        logger.Entries.Should().HaveCount(2);
        logger.Entries.Should().OnlyContain(e =>
            e.Level == LogLevel.Information
            && e.EventId.Id == MetricsLoggerExtensions.MetricsEventId.Id
            && e.EventId.Name == "Metrics");
        logger.Entries[0].Message.Should().Be("requests_total=42");
    }

    [Fact]
    public void LogMetrics_WithCustomEventId_KeepsCallerEventId()
    {
        var logger = new RecordingLogger();

        logger.LogMetrics(new EventId(200, "Throughput"), "throughput_rps={Rps}", 12);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Id.Should().Be(200);
        entry.EventId.Name.Should().Be("Throughput");
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
