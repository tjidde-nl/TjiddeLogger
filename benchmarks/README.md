# Tjidde.Logging benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) benchmarks for the logging path of `TjiddeLogger`: one call from
`ILogger.Log` to the rendered line. They measure the logger, not the console: every logger runs with
`WriteToConsole = false`, so nothing is queued for the console writer. Sensitive data masking is on (the default)
and the customer context is a fixed value, so every line has a customer part.

| Benchmark              | What it logs                                                                                  |
|------------------------|-----------------------------------------------------------------------------------------------|
| `Simple`               | A constant message without properties (`LoggerMessage.Define`).                               |
| `Structured`           | A template with three non-sensitive properties (`{UserId}`, `{ItemCount}`, `{Destination}`).  |
| `SensitiveProperty`    | A template with a sensitive property (`{Password}`): masked, message rebuilt from the template. |
| `StructuredWithScopes` | `Structured` with two active scopes: a text scope and a `MethodName` scope with two properties. |
| `StructuredWithSink`   | `Structured` with one no-op `ILogSink`, so the `TjiddeLogEntry` is built as well.              |
| `Disabled`             | A `Debug` entry on a logger with minimum level `Information`: the `IsEnabled == false` path.   |

Every benchmark runs for both output formats (`Format = Text` and `Format = Json`). The other benchmarks have
no sinks and a framework `LoggerExternalScopeProvider` without active scopes, as in a normal host.

## Running

```
dotnet run -c Release --project benchmarks/Tjidde.Logging.Benchmarks -- --filter '*'
```

Useful options:

- `--filter '*Structured*'`: only the matching benchmarks.
- `--job short`: fewer iterations, for a quick check (less accurate).
- `--artifacts <dir>`: where the reports go (default `BenchmarkDotNet.Artifacts`, ignored by git).

The benchmark project targets `net10.0` and needs the .NET 10 SDK.

## Baseline

Commit `325a711`, before any optimization. Default job, .NET 10.0.12, Intel Core i7-8565U (laptop, 4 cores),
Windows 11. The microsecond results on this machine are noisy (BenchmarkDotNet reports multimodal distributions),
so compare the means as rough figures; the allocations are exact.

| Method               | Format |      Mean | Allocated |
|----------------------|--------|----------:|----------:|
| Simple               | Text   |  4,066 ns |    1224 B |
| Structured           | Text   | 10,722 ns |    1568 B |
| SensitiveProperty    | Text   | 15,507 ns |    2376 B |
| StructuredWithScopes | Text   | 23,864 ns |    2736 B |
| StructuredWithSink   | Text   | 15,735 ns |    1680 B |
| Disabled             | Text   |      7.8 ns |       - |
| Simple               | Json   | 12,218 ns |    2145 B |
| Structured           | Json   | 26,271 ns |    3914 B |
| SensitiveProperty    | Json   | 26,299 ns |    4042 B |
| StructuredWithScopes | Json   | 39,634 ns |    4436 B |
| StructuredWithSink   | Json   | 21,645 ns |    4026 B |
| Disabled             | Json   |     10.5 ns |       - |

Most of the time goes to sensitive data masking: `SensitiveDataMasker.MaskMessage` runs two regular expressions
per sensitive key (17 built-in keys, so 34 regex scans) over the message and over every string property and scope
value, even when the text contains none of the keys.
