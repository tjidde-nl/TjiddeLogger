using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Logging;
using Xunit;

namespace Tjidde.Logging.Tests;

/// <summary>
/// How the standard Microsoft.Extensions.Logging filters (<c>Logging:LogLevel</c> and, through
/// <c>[ProviderAlias("Tjidde")]</c>, <c>Logging:Tjidde:LogLevel</c>) and Tjidde's own (obsolete)
/// <c>TjiddeLogger:MinimumLevel</c> / <c>TjiddeLogger:CategoryMinimumLevels</c> work together:
/// the framework filters first, then Tjidde's own filter, so the stricter of the two wins.
/// </summary>
[Collection("ConsoleOutput")]
public sealed class LogLevelFilteringTests
{
    private const string Category = "My.App.Service";

    /// <summary>
    /// Builds a host-like logging setup (the "Logging" section via AddConfiguration, the "TjiddeLogger" section
    /// via AddTjiddeLogger), runs <paramref name="log"/> and returns the written lines.
    /// </summary>
    private static string[] Run(
        Dictionary<string, string?> settings,
        Action<ILoggerFactory> log,
        Action<ILoggingBuilder>? configureLogging = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddTjiddeLogger(configuration);
                configureLogging?.Invoke(builder);
            });

            // Disposing the container disposes the provider, which flushes the console writer
            using var provider = services.BuildServiceProvider();
            log(provider.GetRequiredService<ILoggerFactory>());
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void LogAllLevels(ILogger logger)
    {
        logger.LogTrace("trace entry");
        logger.LogDebug("debug entry");
        logger.LogInformation("information entry");
        logger.LogWarning("warning entry");
        logger.LogError("error entry");
    }

    private static IEnumerable<string> Messages(string[] lines)
        => lines.Select(line => line[(line.LastIndexOf(": ", StringComparison.Ordinal) + 2)..]);

    [Fact]
    public void ProviderLogLevel_Warning_WithTjiddeMinimumLevel_Debug_WarningWins()
    {
        bool informationEnabled = true;
        var lines = Run(
            new()
            {
                ["Logging:Tjidde:LogLevel:Default"] = "Warning",
                ["TjiddeLogger:MinimumLevel"] = "Debug"
            },
            factory =>
            {
                var logger = factory.CreateLogger(Category);
                informationEnabled = logger.IsEnabled(LogLevel.Information);
                LogAllLevels(logger);
            });

        informationEnabled.Should().BeFalse("the framework filter drops Information before Tjidde's own filter is asked");
        Messages(lines).Should().Equal("warning entry", "error entry");
    }

    [Fact]
    public void ProviderLogLevel_Debug_WithTjiddeMinimumLevel_Warning_WarningWins()
    {
        bool debugEnabled = true;
        var lines = Run(
            new()
            {
                ["Logging:Tjidde:LogLevel:Default"] = "Debug",
                ["TjiddeLogger:MinimumLevel"] = "Warning"
            },
            factory =>
            {
                var logger = factory.CreateLogger(Category);
                debugEnabled = logger.IsEnabled(LogLevel.Debug);
                LogAllLevels(logger);
            });

        debugEnabled.Should().BeFalse("Tjidde's own minimum level still applies after the framework filter");
        Messages(lines).Should().Equal("warning entry", "error entry");
    }

    [Fact]
    public void TjiddeMinimumLevel_Debug_WithoutLoggingRules_StillDropsDebug()
    {
        var lines = Run(
            new() { ["TjiddeLogger:MinimumLevel"] = "Debug" },
            factory => LogAllLevels(factory.CreateLogger(Category)));

        Messages(lines).Should().Equal(
            new[] { "information entry", "warning entry", "error entry" },
            "the framework's default minimum level is Information");
    }

    [Fact]
    public void ProviderLogLevel_Alone_FiltersPerCategory()
    {
        var lines = Run(
            new()
            {
                ["Logging:Tjidde:LogLevel:Default"] = "Information",
                ["Logging:Tjidde:LogLevel:My.App.Noisy"] = "Error",
                ["Logging:Tjidde:LogLevel:My.App.Orders.OrderService"] = "Debug"
            },
            factory =>
            {
                factory.CreateLogger(Category).LogDebug("service debug");
                factory.CreateLogger(Category).LogInformation("service information");
                factory.CreateLogger("My.App.Noisy.Poller").LogWarning("noisy warning");
                factory.CreateLogger("My.App.Noisy.Poller").LogError("noisy error");
                factory.CreateLogger("My.App.Orders.OrderService").LogDebug("orders debug");
            });

        Messages(lines).Should().Equal("service information", "noisy error", "orders debug");
    }

    [Fact]
    public void ProviderLogLevel_OverridesGlobalLogLevel_ForTjiddeOnly()
    {
        var lines = Run(
            new()
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:Tjidde:LogLevel:Default"] = "Debug"
            },
            factory => LogAllLevels(factory.CreateLogger(Category)));

        Messages(lines).Should().Equal("debug entry", "information entry", "warning entry", "error entry");
    }

    [Fact]
    public void GlobalLogLevel_WithoutProviderSection_AppliesToTjidde()
    {
        var lines = Run(
            new() { ["Logging:LogLevel:Default"] = "Warning" },
            factory => LogAllLevels(factory.CreateLogger(Category)));

        Messages(lines).Should().Equal("warning entry", "error entry");
    }

    [Fact]
    public void TjiddeCategoryMinimumLevels_StricterThanProviderLogLevel_Wins()
    {
        var lines = Run(
            new()
            {
                ["Logging:Tjidde:LogLevel:Default"] = "Trace",
                ["TjiddeLogger:CategoryMinimumLevels:My.App"] = "Error"
            },
            factory =>
            {
                LogAllLevels(factory.CreateLogger(Category));
                factory.CreateLogger("Other.Component").LogTrace("other trace");
            });

        Messages(lines).Should().Equal("error entry", "other trace");
    }

    [Fact]
    public void AddFilterForTjiddeProvider_InCode_WorksLikeProviderLogLevel()
    {
        var lines = Run(
            new(),
            factory => LogAllLevels(factory.CreateLogger(Category)),
            builder => builder.AddFilter<TjiddeLoggerProvider>("My.App", LogLevel.Error));

        Messages(lines).Should().Equal("error entry");
    }

    [Fact]
    public void Metrics_AreFilteredByProviderLogLevel_ButNotByTjiddeMinimumLevel()
    {
        var droppedByFramework = Run(
            new() { ["Logging:Tjidde:LogLevel:Default"] = "Warning" },
            factory => factory.CreateLogger(Category).LogMetrics("requests_total={Count}", 1));

        var keptByTjidde = Run(
            new()
            {
                ["Logging:Tjidde:LogLevel:Default"] = "Information",
                ["TjiddeLogger:MinimumLevel"] = "Critical"
            },
            factory => factory.CreateLogger(Category).LogMetrics("requests_total={Count}", 2));

        droppedByFramework.Should().BeEmpty("metrics entries are Information entries for the framework filter");
        keptByTjidde.Should().ContainSingle().Which.Should().Contain("[METRICS]").And.EndWith("requests_total=2");
    }
}
