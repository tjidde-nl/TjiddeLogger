using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Tjidde.Logging.Sinks;
using Xunit;

namespace Tjidde.Logging.Tests;

/// <summary>Extra <see cref="ILogSink"/>s next to the console, and the built-in <see cref="InMemoryLogSink"/>.</summary>
[Collection("ConsoleOutput")]
public sealed class LogSinkTests
{
    private static string CaptureConsoleOutput(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
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

    private static string[] Lines(string output)
        => output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static TjiddeLoggerProvider CreateProvider(TestOptionsMonitor monitor, params ILogSink[] sinks)
        => new(monitor, new FixedCustomer("Acme"), new MaskedKeysStore(), TimeProvider.System, sinks);

    [Fact]
    public void ExtraSink_ReceivesEntryWithMaskedValuesOnly()
    {
        var sink = new InMemoryLogSink();
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions());

        var output = CaptureConsoleOutput(() =>
        {
            using var provider = CreateProvider(monitor, sink);
            provider.SetScopeProvider(new LoggerExternalScopeProvider());
            var logger = provider.CreateLogger("My.App.LoginService");
            using (logger.BeginScope(new Dictionary<string, object?> { ["MethodName"] = "SignIn" }))
            {
                logger.LogWarning(
                    new EventId(42, "Login"),
                    new InvalidOperationException("failed with password=hunter2"),
                    "Login for {User} with {Password} and token=abc123", "alice", "s3cret!");
            }
        });

        var entry = sink.GetSnapshot().Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Should().Be(new EventId(42, "Login"));
        entry.Category.Should().Be("My.App.LoginService");
        entry.ClassName.Should().Be("LoginService");
        entry.MethodName.Should().Be("SignIn");
        entry.Customer.Should().Be("Acme");
        entry.OutputFormat.Should().Be(TjiddeLogOutputFormat.Text);
        entry.IsMetrics.Should().BeFalse();
        entry.Timestamp.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromMinutes(1));

        entry.Message.Should().Contain("alice").And.Contain("[REDACTED]");
        entry.FormattedException.Should().Contain("InvalidOperationException").And.Contain("[REDACTED]");
        foreach (var text in new[] { entry.Message, entry.FormattedException!, entry.RenderedLine })
        {
            text.Should().NotContain("s3cret!");
            text.Should().NotContain("abc123");
            text.Should().NotContain("hunter2");
        }

        // The sink gets exactly the line the console gets.
        Lines(output).Should().ContainSingle().Which.Should().Be(entry.RenderedLine);
    }

    [Fact]
    public void JsonFormat_RenderedLineIsJsonAndMessageIsMasked()
    {
        var sink = new InMemoryLogSink();
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions { OutputFormat = TjiddeLogOutputFormat.Json, WriteToConsole = false });

        using (var provider = CreateProvider(monitor, sink))
            provider.CreateLogger("Json.Category").LogInformation("Connect with {Password}", "p@ss");

        var entry = sink.GetSnapshot().Should().ContainSingle().Subject;
        entry.OutputFormat.Should().Be(TjiddeLogOutputFormat.Json);
        entry.Message.Should().NotContain("p@ss").And.Contain("[REDACTED]");
        entry.RenderedLine.Should().NotContain("p@ss");

        using var json = JsonDocument.Parse(entry.RenderedLine);
        json.RootElement.GetProperty("message").GetString().Should().Be(entry.Message);
    }

    [Fact]
    public void MetricsEntry_IsRecognizedByEventId()
    {
        var sink = new InMemoryLogSink();
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions { WriteToConsole = false });

        using (var provider = CreateProvider(monitor, sink))
            provider.CreateLogger("Metrics.Category").LogMetrics("requests_total={Count}", 42);

        var entry = sink.GetSnapshot().Should().ContainSingle().Subject;
        entry.IsMetrics.Should().BeTrue();
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Should().Be(MetricsLoggerExtensions.MetricsEventId);
        entry.RenderedLine.Should().Contain("[METRICS]");
    }

    [Fact]
    public void WriteToConsoleFalse_WritesOnlyToSinks_AndReloadSwitchesTheConsoleBackOn()
    {
        var sink = new InMemoryLogSink();
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions { WriteToConsole = false });

        var output = CaptureConsoleOutput(() =>
        {
            using var provider = CreateProvider(monitor, sink);
            var logger = provider.CreateLogger("Console.Switch");

            logger.LogInformation("first, sink only");

            monitor.Set(new TjiddeLoggerOptions { WriteToConsole = true });
            logger.LogInformation("second, console and sink");

            monitor.Set(new TjiddeLoggerOptions { WriteToConsole = false });
            logger.LogInformation("third, sink only");
        });

        Lines(output).Should().ContainSingle().Which.Should().EndWith("second, console and sink");
        sink.GetSnapshot().Select(e => e.Message)
            .Should().Equal("first, sink only", "second, console and sink", "third, sink only");
    }

    [Fact]
    public void WriteToConsole_IsBoundFromConfigurationAndReloads()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TjiddeLogger:WriteToConsole"] = "false" })
            .Build();

        InMemoryLogSink? sink = null;
        var output = CaptureConsoleOutput(() =>
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddTjiddeLogger(configuration).AddTjiddeInMemorySink());
            using var provider = services.BuildServiceProvider();
            sink = provider.GetRequiredService<InMemoryLogSink>();
            var logger = provider.GetRequiredService<ILogger<LogSinkTests>>();

            logger.LogInformation("hidden from console");
            configuration["TjiddeLogger:WriteToConsole"] = "true";
            configuration.Reload();
            logger.LogInformation("visible on console");
        });

        Lines(output).Should().ContainSingle().Which.Should().EndWith("visible on console");
        sink!.Count.Should().Be(2);
    }

    [Fact]
    public void ThrowingSink_DoesNotBreakLogging_AndOtherSinksAndConsoleStillReceiveEntries()
    {
        var throwing = new ThrowingSink();
        var sink = new InMemoryLogSink();
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions());

        var output = CaptureConsoleOutput(() =>
        {
            using var provider = CreateProvider(monitor, throwing, sink);
            var logger = provider.CreateLogger("Throwing.Sink");

            logger.Invoking(l => l.LogError("one")).Should().NotThrow();
            logger.Invoking(l => l.LogError("two")).Should().NotThrow();
        });

        throwing.Calls.Should().Be(2);
        sink.GetSnapshot().Select(e => e.Message).Should().Equal("one", "two");
        Lines(output).Should().HaveCount(2);
    }

    [Fact]
    public void SinkThatLogs_DoesNotRecurse()
    {
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions { WriteToConsole = false });
        var recursive = new RecursiveSink();
        var sink = new InMemoryLogSink();

        using (var provider = CreateProvider(monitor, recursive, sink))
        {
            recursive.Logger = provider.CreateLogger("Recursive.Sink");
            recursive.Logger.LogInformation("outer");
        }

        recursive.Calls.Should().Be(1);
        sink.GetSnapshot().Select(e => e.Message).Should().Equal("outer");
    }

    [Theory]
    [InlineData(TjiddeLogOutputFormat.Text)]
    [InlineData(TjiddeLogOutputFormat.Json)]
    public void EntryLoggedWhileReadingScopes_DoesNotAffectTheOuterEntry(TjiddeLogOutputFormat format)
    {
        // A scope value whose ToString() logs: the nested call runs while the outer entry is reading its scopes,
        // on the same thread, and both entries must still get their own method name, scopes and line.
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions { WriteToConsole = false, OutputFormat = format });
        var sink = new InMemoryLogSink();
        var value = new LoggingValue();

        using (var provider = CreateProvider(monitor, sink))
        {
            provider.SetScopeProvider(new LoggerExternalScopeProvider());
            var logger = provider.CreateLogger("My.App.OrderService");
            value.Logger = logger;
            using (logger.BeginScope("Order pipeline"))
            using (logger.BeginScope(new Dictionary<string, object?> { ["MethodName"] = "Process", ["Item"] = value }))
            {
                logger.LogInformation("outer {Count}", 1);
            }
        }

        var entries = sink.GetSnapshot();
        entries.Select(e => e.Message).Should().Equal("inner [REDACTED]", "outer 1");
        entries.Select(e => e.MethodName).Should().Equal("Process", "Process");
        if (format == TjiddeLogOutputFormat.Text)
        {
            entries[0].RenderedLine.Should().EndWith("inner [REDACTED] [Masked: Password] | Scopes: Order pipeline > Item=value");
            entries[1].RenderedLine.Should().EndWith("outer 1 | Scopes: Order pipeline > Item=value");
        }
        else
        {
            foreach (var entry in entries)
            {
                using var json = JsonDocument.Parse(entry.RenderedLine);
                json.RootElement.GetProperty("scopes").EnumerateArray().Select(e => e.GetString())
                    .Should().Equal("Order pipeline", "Item=value");
            }
        }
    }

    private sealed class LoggingValue
    {
        private bool _logged;
        public ILogger? Logger;

        public override string ToString()
        {
            if (!_logged)
            {
                _logged = true;
                Logger?.LogInformation("inner {Password}", "hunter2");
            }

            return "value";
        }
    }

    [Fact]
    public void DependencyInjection_PassesEntriesToEveryRegisteredSink()
    {
        var instance = new InMemoryLogSink(10);
        var services = new ServiceCollection();
        services.AddSingleton<ILogSink, CountingSink>();
        services.AddLogging(builder => builder
            .AddTjiddeLogger(o => o.WriteToConsole = false)
            .AddTjiddeInMemorySink(capacity: 5)
            .AddTjiddeInMemorySink()
            .AddTjiddeSink<CountingSink2>()
            .AddTjiddeSink<CountingSink2>()
            .AddTjiddeSink(instance));

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<LogSinkTests>>();
        logger.LogInformation("hello");

        var inMemory = provider.GetRequiredService<InMemoryLogSink>();
        inMemory.Capacity.Should().Be(5, "the first registration wins");
        inMemory.Count.Should().Be(1);
        instance.Count.Should().Be(1);
        provider.GetServices<ILogSink>().OfType<CountingSink>().Single().Calls.Should().Be(1);
        provider.GetRequiredService<CountingSink2>().Calls.Should().Be(1, "registering the same sink type twice has no effect");
    }

    [Fact]
    public void InMemoryLogSink_KeepsOnlyTheNewestEntriesUpToCapacity()
    {
        var sink = new InMemoryLogSink(3);

        for (var i = 0; i < 10; i++)
            sink.Write(new TjiddeLogEntry { Message = $"m{i}" });

        sink.Capacity.Should().Be(3);
        sink.Count.Should().Be(3);
        sink.GetSnapshot().Select(e => e.Message).Should().Equal("m7", "m8", "m9");
    }

    [Fact]
    public void InMemoryLogSink_RejectsCapacityBelowOne()
    {
        FluentActions.Invoking(() => new InMemoryLogSink(0)).Should().Throw<ArgumentOutOfRangeException>();
        new InMemoryLogSink().Capacity.Should().Be(InMemoryLogSink.DefaultCapacity);
    }

    [Fact]
    public void InMemoryLogSink_SnapshotIsACopy_AndEventsAreRaised()
    {
        var sink = new InMemoryLogSink();
        var added = new List<TjiddeLogEntry>();
        var cleared = 0;
        sink.EntryAdded += (_, e) => added.Add(e);
        sink.Cleared += (_, _) => cleared++;

        var entry = new TjiddeLogEntry { Message = "a" };
        sink.Write(entry);
        var snapshot = sink.GetSnapshot();
        sink.Write(new TjiddeLogEntry { Message = "b" });

        snapshot.Should().ContainSingle();
        added.Select(e => e.Message).Should().Equal("a", "b");
        added[0].Should().BeSameAs(entry);

        sink.Clear();
        sink.Count.Should().Be(0);
        snapshot.Should().ContainSingle();
        cleared.Should().Be(1);
    }

    [Fact]
    public async Task InMemoryLogSink_IsThreadSafe()
    {
        const int threads = 8;
        const int perThread = 2000;
        var bounded = new InMemoryLogSink(500);
        var unbounded = new InMemoryLogSink(threads * perThread);
        var errors = new ConcurrentQueue<Exception>();
        using var done = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                try
                {
                    bounded.GetSnapshot().Count.Should().BeLessThanOrEqualTo(500);
                    _ = unbounded.Count;
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            }
        });

        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            for (var i = 0; i < perThread; i++)
            {
                var entry = new TjiddeLogEntry { Message = $"{t}:{i}" };
                bounded.Write(entry);
                unbounded.Write(entry);
            }
        });

        done.Cancel();
        await reader.WaitAsync(TimeSpan.FromSeconds(10));

        errors.Should().BeEmpty();
        bounded.Count.Should().Be(500);
        unbounded.GetSnapshot().Select(e => e.Message).Should().OnlyHaveUniqueItems().And.HaveCount(threads * perThread);
    }

    private sealed class FixedCustomer(string customer) : ICustomerContextAccessor
    {
        public string? GetCustomerContext() => customer;
    }

    private sealed class ThrowingSink : ILogSink
    {
        public int Calls;

        public void Write(TjiddeLogEntry entry)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("sink failure");
        }
    }

    private sealed class RecursiveSink : ILogSink
    {
        public int Calls;
        public ILogger? Logger;

        public void Write(TjiddeLogEntry entry)
        {
            Interlocked.Increment(ref Calls);
            Logger?.LogInformation("logged from inside a sink");
        }
    }

    private sealed class CountingSink : ILogSink
    {
        public int Calls;

        public void Write(TjiddeLogEntry entry) => Interlocked.Increment(ref Calls);
    }

    private sealed class CountingSink2 : ILogSink
    {
        public int Calls;

        public void Write(TjiddeLogEntry entry) => Interlocked.Increment(ref Calls);
    }
}
