using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Tjidde.Logging.Sinks;

namespace Tjidde.Logging.Benchmarks;

/// <summary>
/// Measures one log call through <see cref="TjiddeLogger"/>, from <c>ILogger.Log</c> to the rendered line.
/// The console is off (<see cref="TjiddeLoggerOptions.WriteToConsole"/> = false) so console I/O is not measured;
/// sensitive data masking is on (the default). Every scenario runs for both output formats.
/// </summary>
[MemoryDiagnoser]
public class LoggerBenchmarks
{
    private static readonly Action<ILogger, Exception?> SimpleMessage =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, "Simple"), "Order processed successfully");

    private static readonly Action<ILogger, int, int, string, Exception?> StructuredMessage =
        LoggerMessage.Define<int, int, string>(LogLevel.Information, new EventId(2, "Structured"),
            "User {UserId} ordered {ItemCount} items for {Destination}");

    private static readonly Action<ILogger, string, string, Exception?> SensitiveMessage =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(3, "Sensitive"),
            "Login for {UserName} with {Password}");

    private static readonly Action<ILogger, int, Exception?> DebugMessage =
        LoggerMessage.Define<int>(LogLevel.Debug, new EventId(4, "Debug"), "Cache lookup for {Key}");

    private readonly List<TjiddeLoggerProvider> _providers = [];

    private ILogger _logger = null!;
    private ILogger _scopedLogger = null!;
    private ILogger _sinkLogger = null!;
    private ILogger _infoLevelLogger = null!;

    [Params(TjiddeLogOutputFormat.Text, TjiddeLogOutputFormat.Json)]
    public TjiddeLogOutputFormat Format { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Logger without scopes: IncludeScopes is on (the default), but no scope is active.
        _logger = CreateLogger(new TjiddeLoggerOptions());

        // Logger with a text scope and a structured MethodName scope active.
        _scopedLogger = CreateLogger(new TjiddeLoggerOptions(), scopeProvider: new FixedScopeProvider());

        // Logger with one sink that discards the entries, so the TjiddeLogEntry is built.
        _sinkLogger = CreateLogger(new TjiddeLoggerOptions(), sinks: [new NoOpSink()]);

        // Logger with minimum level Information, for the IsEnabled == false path.
#pragma warning disable CS0618 // MinimumLevel is obsolete but still honored; it is the cheapest way to filter here
        _infoLevelLogger = CreateLogger(new TjiddeLoggerOptions { MinimumLevel = LogLevel.Information });
#pragma warning restore CS0618
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var provider in _providers)
            provider.Dispose();
    }

    private ILogger CreateLogger(TjiddeLoggerOptions options, IExternalScopeProvider? scopeProvider = null, ILogSink[]? sinks = null)
    {
        options.OutputFormat = Format;
        options.WriteToConsole = false;

        var provider = new TjiddeLoggerProvider(
            new StaticOptionsMonitor<TjiddeLoggerOptions>(options),
            new FixedCustomerContextAccessor("ACME"),
            new MaskedKeysStore(),
            TimeProvider.System,
            sinks);

        // Without fixed scopes: the framework's own scope provider with no scope pushed, as in a host.
        provider.SetScopeProvider(scopeProvider ?? new LoggerExternalScopeProvider());
        _providers.Add(provider);
        return provider.CreateLogger("Tjidde.Logging.Benchmarks.OrderService");
    }

    /// <summary>A constant message without properties.</summary>
    [Benchmark(Baseline = true)]
    public void Simple() => SimpleMessage(_logger, null);

    /// <summary>A message template with three non-sensitive properties.</summary>
    [Benchmark]
    public void Structured() => StructuredMessage(_logger, 42, 3, "Amsterdam", null);

    /// <summary>A message template with a sensitive property: masked and the message rebuilt from the template.</summary>
    [Benchmark]
    public void SensitiveProperty() => SensitiveMessage(_logger, "alice", "hunter2!", null);

    /// <summary>The structured message with two active scopes (text and MethodName/properties).</summary>
    [Benchmark]
    public void StructuredWithScopes() => StructuredMessage(_scopedLogger, 42, 3, "Amsterdam", null);

    /// <summary>The structured message with one (no-op) sink registered, so the sink entry is built too.</summary>
    [Benchmark]
    public void StructuredWithSink() => StructuredMessage(_sinkLogger, 42, 3, "Amsterdam", null);

    /// <summary>A debug entry on a logger whose minimum level is Information: IsEnabled is false.</summary>
    [Benchmark]
    public void Disabled() => DebugMessage(_infoLevelLogger, 7, null);
}
