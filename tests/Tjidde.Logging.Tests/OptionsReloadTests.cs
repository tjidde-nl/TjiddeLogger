using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Xunit;

namespace Tjidde.Logging.Tests;

/// <summary>
/// Options changes (for example appsettings.json with reloadOnChange) apply to loggers that already exist.
/// </summary>
[Collection("ConsoleOutput")]
public sealed class OptionsReloadTests
{
    private static string[] CaptureConsoleLines(Action action)
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

        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void ConfigurationReload_AppliesOutputFormatMinimumLevelAndPlaceholderToExistingLogger()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TjiddeLogger:OutputFormat"] = "Text",
                ["TjiddeLogger:MinimumLevel"] = "Information",
                ["TjiddeLogger:MaskPlaceholder"] = "[REDACTED]"
            })
            .Build();

        bool enabledBefore = true, enabledAfter = false;
        var lines = CaptureConsoleLines(() =>
        {
            var services = new ServiceCollection();
            // Trace, so only Tjidde's own minimum level filters (the framework default is Information)
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddTjiddeLogger(configuration));
            using var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("My.App.ReloadService");

            enabledBefore = logger.IsEnabled(LogLevel.Debug);
            logger.LogDebug("dropped before reload");
            logger.LogInformation("before reload password=abc123");

            configuration["TjiddeLogger:OutputFormat"] = "Json";
            configuration["TjiddeLogger:MinimumLevel"] = "Debug";
            configuration["TjiddeLogger:MaskPlaceholder"] = "***";
            configuration.Reload();

            enabledAfter = logger.IsEnabled(LogLevel.Debug);
            logger.LogDebug("after reload password=abc123");
        });

        enabledBefore.Should().BeFalse();
        enabledAfter.Should().BeTrue();

        lines.Should().HaveCount(2, "the debug entry before the reload is below the minimum level");
        lines[0].Should().StartWith("20").And.EndWith("before reload password=[REDACTED]");

        using var json = JsonDocument.Parse(lines[1]);
        json.RootElement.GetProperty("level").GetString().Should().Be("DEBUG");
        json.RootElement.GetProperty("message").GetString().Should().Be("after reload password=***");
    }

    [Fact]
    public void OptionsMonitorChange_AppliesCategoryLevelsToExistingLogger()
    {
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions { MinimumLevel = LogLevel.Information });
        using var provider = new TjiddeLoggerProvider(monitor, new AsyncLocalCustomerContextAccessor(), new GlobalMaskedKeysAccessor());
        var logger = provider.CreateLogger("My.App.Service");

        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();

        var updated = new TjiddeLoggerOptions { MinimumLevel = LogLevel.Information };
        updated.CategoryMinimumLevels["My.App"] = LogLevel.Trace;
        monitor.Set(updated);

        logger.IsEnabled(LogLevel.Trace).Should().BeTrue();

        monitor.Set(new TjiddeLoggerOptions { MinimumLevel = LogLevel.Error });

        logger.IsEnabled(LogLevel.Warning).Should().BeFalse();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
    }

    [Fact]
    public void OptionsMonitorChange_ForNamedOptions_IsIgnored()
    {
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions { MinimumLevel = LogLevel.Information });
        using var provider = new TjiddeLoggerProvider(monitor, new AsyncLocalCustomerContextAccessor(), new GlobalMaskedKeysAccessor());
        var logger = provider.CreateLogger("My.App.Service");

        monitor.Set(new TjiddeLoggerOptions { MinimumLevel = LogLevel.Trace }, name: "Other");

        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
    }

    [Fact]
    public void Dispose_StopsListeningForOptionsChanges()
    {
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions());
        var provider = new TjiddeLoggerProvider(monitor, new AsyncLocalCustomerContextAccessor(), new GlobalMaskedKeysAccessor());
        monitor.ListenerCount.Should().Be(1);

        provider.Dispose();

        monitor.ListenerCount.Should().Be(0);
        monitor.Invoking(m => m.Set(new TjiddeLoggerOptions())).Should().NotThrow();
    }

    /// <summary>An <see cref="IOptionsMonitor{TOptions}"/> whose value the test replaces explicitly.</summary>
    private sealed class TestOptionsMonitor : IOptionsMonitor<TjiddeLoggerOptions>
    {
        private readonly List<Action<TjiddeLoggerOptions, string?>> _listeners = [];

        public TestOptionsMonitor(TjiddeLoggerOptions initial) => CurrentValue = initial;

        public TjiddeLoggerOptions CurrentValue { get; private set; }

        public int ListenerCount
        {
            get
            {
                lock (_listeners)
                    return _listeners.Count;
            }
        }

        public TjiddeLoggerOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<TjiddeLoggerOptions, string?> listener)
        {
            lock (_listeners)
                _listeners.Add(listener);

            return new Registration(() =>
            {
                lock (_listeners)
                    _listeners.Remove(listener);
            });
        }

        public void Set(TjiddeLoggerOptions options, string name = "")
        {
            if (name.Length == 0)
                CurrentValue = options;

            Action<TjiddeLoggerOptions, string?>[] listeners;
            lock (_listeners)
                listeners = _listeners.ToArray();

            foreach (var listener in listeners)
                listener(options, name);
        }

        private sealed class Registration(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
