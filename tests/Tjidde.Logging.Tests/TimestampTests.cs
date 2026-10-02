using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Xunit;

namespace Tjidde.Logging.Tests;

/// <summary>
/// Timestamps come from a <see cref="TimeProvider"/> (optionally from DI) and are local time by default,
/// or UTC with <see cref="TjiddeLoggerOptions.UseUtcTimestamp"/>.
/// </summary>
[Collection("ConsoleOutput")]
public sealed class TimestampTests
{
    // 10:20:30 UTC = 12:20:30 in the fixed +02:00 test zone
    private static readonly DateTimeOffset Start = new(2026, 3, 15, 10, 20, 30, TimeSpan.Zero);

    private static readonly TimeZoneInfo PlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("Tjidde.Test+02", TimeSpan.FromHours(2), "Test +02:00", "Test +02:00");

    private static FakeTimeProvider CreateClock()
    {
        var clock = new FakeTimeProvider(Start);
        clock.SetLocalTimeZone(PlusTwo);
        return clock;
    }

    private static string[] Capture(TjiddeLoggerOptions options, TimeProvider timeProvider, Action<ILogger> log)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            // Disposing the provider flushes its console writer
            using var provider = new TjiddeLoggerProvider(
                new TestOptionsMonitor(options), new AsyncLocalCustomerContextAccessor(), new GlobalMaskedKeysAccessor(), timeProvider);
            log(provider.CreateLogger("My.App.ClockService"));
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string JsonTimestamp(string line)
    {
        using var json = JsonDocument.Parse(line);
        return json.RootElement.GetProperty("@timestamp").GetString()!;
    }

    [Fact]
    public void UseUtcTimestamp_DefaultsToFalse()
    {
        new TjiddeLoggerOptions().UseUtcTimestamp.Should().BeFalse();
    }

    [Fact]
    public void Text_ByDefault_WritesLocalTimeOfTimeProvider()
    {
        var lines = Capture(new TjiddeLoggerOptions(), CreateClock(), logger => logger.LogInformation("hello"));

        lines.Should().ContainSingle().Which.Should().StartWith("2026-03-15: 12:20:30: [INFORMATION]");
    }

    [Fact]
    public void Text_WithUseUtcTimestamp_WritesUtcTime()
    {
        var lines = Capture(new TjiddeLoggerOptions { UseUtcTimestamp = true }, CreateClock(), logger => logger.LogInformation("hello"));

        lines.Should().ContainSingle().Which.Should().StartWith("2026-03-15: 10:20:30: [INFORMATION]");
    }

    [Fact]
    public void Json_ByDefault_WritesIso8601WithLocalOffset()
    {
        var lines = Capture(
            new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json },
            CreateClock(),
            logger => logger.LogInformation("hello"));

        var timestamp = JsonTimestamp(lines.Should().ContainSingle().Subject);
        timestamp.Should().Be("2026-03-15T12:20:30.0000000+02:00");
        DateTimeOffset.Parse(timestamp).Should().Be(Start);
    }

    [Fact]
    public void Json_WithUseUtcTimestamp_WritesIso8601WithZeroOffset()
    {
        var lines = Capture(
            new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json, UseUtcTimestamp = true },
            CreateClock(),
            logger => logger.LogInformation("hello"));

        JsonTimestamp(lines.Should().ContainSingle().Subject).Should().Be("2026-03-15T10:20:30.0000000+00:00");
    }

    [Fact]
    public void Timestamp_FollowsTheTimeProvider()
    {
        var clock = CreateClock();
        var lines = Capture(
            new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json, UseUtcTimestamp = true },
            clock,
            logger =>
            {
                logger.LogInformation("first");
                clock.Advance(TimeSpan.FromMinutes(90.5));
                logger.LogInformation("second");
            });

        lines.Should().HaveCount(2);
        JsonTimestamp(lines[0]).Should().Be("2026-03-15T10:20:30.0000000+00:00");
        JsonTimestamp(lines[1]).Should().Be("2026-03-15T11:51:00.0000000+00:00");
    }

    [Fact]
    public void ThrowingTimeProvider_DoesNotBreakLogging()
    {
        string[] lines = [];
        var act = () => lines = Capture(new TjiddeLoggerOptions(), new ThrowingTimeProvider(), logger => logger.LogInformation("still logged"));

        act.Should().NotThrow();
        lines.Should().ContainSingle().Which.Should().EndWith("still logged");
    }

    [Fact]
    public void Provider_WithNullTimeProvider_Throws()
    {
        var act = () => new TjiddeLoggerProvider(
            new TestOptionsMonitor(new TjiddeLoggerOptions()),
            new AsyncLocalCustomerContextAccessor(),
            new GlobalMaskedKeysAccessor(),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void AddTjiddeLogger_UsesTimeProviderFromDI()
    {
        var clock = CreateClock();
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(clock);
            services.AddLogging(builder => builder.AddTjiddeLogger(options =>
            {
                options.OutputFormat = TjiddeLogOutputFormat.Json;
                options.UseUtcTimestamp = true;
            }));

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("My.App.ClockService").LogInformation("from DI");
        }
        finally
        {
            Console.SetOut(original);
        }

        var line = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle().Subject;
        JsonTimestamp(line).Should().Be("2026-03-15T10:20:30.0000000+00:00");
    }

    [Fact]
    public void AddTjiddeLogger_WithoutTimeProviderInDI_UsesSystemClock()
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddTjiddeLogger(options => options.OutputFormat = TjiddeLogOutputFormat.Json));

            using var provider = services.BuildServiceProvider();
            provider.GetServices<ILoggerProvider>().Should().ContainSingle(p => p is TjiddeLoggerProvider);
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("My.App.ClockService").LogInformation("system clock");
        }
        finally
        {
            Console.SetOut(original);
        }

        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        var line = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle().Subject;
        var timestamp = DateTimeOffset.Parse(JsonTimestamp(line));
        timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        timestamp.Offset.Should().Be(TimeZoneInfo.Local.GetUtcOffset(timestamp));
    }

    [Fact]
    public void AddTjiddeLogger_CalledTwice_RegistersOneProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(CreateClock());
        services.AddLogging(builder => builder.AddTjiddeLogger().AddTjiddeLogger(_ => { }));

        using var provider = services.BuildServiceProvider();

        provider.GetServices<ILoggerProvider>().Should().ContainSingle(p => p is TjiddeLoggerProvider);
    }

    [Fact]
    public void UseUtcTimestamp_BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TjiddeLogger:UseUtcTimestamp"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTjiddeLogger(configuration));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<TjiddeLoggerOptions>>().Value.UseUtcTimestamp.Should().BeTrue();
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("clock failure");

        public override TimeZoneInfo LocalTimeZone => throw new InvalidOperationException("clock failure");
    }
}
