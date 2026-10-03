# Tjidde.Logging

[![NuGet](https://img.shields.io/nuget/v/Tjidde.Logging.svg)](https://www.nuget.org/packages/Tjidde.Logging)
[![CI](https://github.com/tjidde-nl/TjiddeLogger/actions/workflows/ci.yml/badge.svg)](https://github.com/tjidde-nl/TjiddeLogger/actions/workflows/ci.yml)

A reusable NuGet package for standardized, structured logging in .NET applications.  
Integrates naturally with the Microsoft.Extensions.Logging pipeline.

> **Using an AI coding assistant?** Point it to [AI-INTEGRATION.md](https://github.com/tjidde-nl/TjiddeLogger/blob/main/docs/AI-INTEGRATION.md). The same guide ships inside the NuGet package, at `~/.nuget/packages/tjidde.logging/<version>/AI-INTEGRATION.md` (on Windows: `%UserProfile%\.nuget\packages\tjidde.logging\<version>\AI-INTEGRATION.md`).

---

## Purpose

Tjidde.Logging provides a consistent logging experience across .NET applications — web APIs, workers, background services, and more. It standardizes log output format, enriches entries with customer/tenant context, masks sensitive data, and formats exceptions in a clear and readable way.

---

## Supported .NET Versions

| Version  | Supported |
|----------|-----------|
| .NET 7   | ✅        |
| .NET 8   | ✅        |
| .NET 9   | ✅        |
| .NET 10  | ✅        |

> Multi-targeting (net7.0;net8.0;net9.0;net10.0) is configured in the package project and intended for CI builds where all SDKs are available.

---

## Installation

```bash
dotnet add package Tjidde.Logging
```

Or via PackageReference:

```xml
<PackageReference Include="Tjidde.Logging" Version="*" />
```

---

## Usage

### Basic registration

```csharp
services.AddLogging(builder => builder.AddTjiddeLogger());
```

### With options

```csharp
services.AddLogging(builder => builder.AddTjiddeLogger(options =>
{
    options.IncludeScopes = true;
    options.IncludeStackTrace = true;
    options.IncludeInnerExceptions = true;
    options.EnableSensitiveDataMasking = true;
    options.MaskPlaceholder = "[REDACTED]";
    options.AdditionalSensitiveKeys = ["internalToken", "tenantSecret"];
}));
```

### Log levels

Filter log levels with the standard `Logging` section of `appsettings.json`. `Logging:Tjidde:LogLevel` applies to Tjidde.Logging only (the provider alias is `Tjidde`); `Logging:LogLevel` applies to every provider:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    },
    "Tjidde": {
      "LogLevel": {
        "Default": "Information",
        "MyCompany.MyApp.Services.OrderService": "Debug",
        "MyCompany.MyApp.Polling": "Warning"
      }
    }
  }
}
```

In code, `builder.AddFilter<TjiddeLoggerProvider>("MyCompany.MyApp", LogLevel.Debug)` does the same.

`TjiddeLoggerOptions.MinimumLevel` and `CategoryMinimumLevels` (`TjiddeLogger:MinimumLevel`, `TjiddeLogger:CategoryMinimumLevels`) are obsolete and will be removed in 2.0. They still work: the `Logging` filter runs first, then Tjidde's own filter, so an entry must pass both and the stricter level wins. Move these values to `Logging:Tjidde:LogLevel`.

### Accessors and DI (recommended)

The logger reads the customer context and the runtime masked keys through two DI abstractions: `ICustomerContextAccessor` and `IMaskedKeysAccessor`. For testable code, use these instead of the static classes:

- **Customer context**: register your own `ICustomerContextAccessor` (for example one that reads the HTTP context, see below). In tests, register a fake that returns a fixed value.
- **Masked keys**: call `UseIsolatedMaskedKeys()` and inject `MaskedKeysStore`. Each host (and each test's `ServiceProvider`) gets its own keys; changes apply immediately to that host's existing loggers.

```csharp
services.AddLogging(builder => builder.AddTjiddeLogger().UseIsolatedMaskedKeys());

public sealed class VaultService(MaskedKeysStore maskedKeys)
{
    public void OnSecretLoaded(string secret) => maskedKeys.Add(secret);
}
```

The static `CustomerContext` and `MaskedKeysContext` remain the defaults and are the convenience variant: no DI needed, but `MaskedKeysContext` is shared by every host and test in the process.

### Setting customer context

Use `CustomerContext.Set(...)` at the start of a request, job, or operation. It flows through the async call chain automatically. This works with the default `AsyncLocalCustomerContextAccessor`.

```csharp
// In middleware, a job handler, or service entry point:
CustomerContext.Set("AcmeCorp");

// Later in any service in the same async flow:
_logger.LogInformation("Order placed successfully.");
// Output: 2024-03-17: 14:22:01: [INFORMATION] Class=>OrderService: Client=>AcmeCorp: Order placed successfully.

// Clear when done (optional — AsyncLocal is scoped to the flow):
CustomerContext.Clear();
```

### Custom customer context accessor

For HTTP-based apps you may want to derive the customer from the HTTP context. Implement `ICustomerContextAccessor` and register it before calling `AddTjiddeLogger`:

```csharp
public class HttpCustomerContextAccessor : ICustomerContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCustomerContextAccessor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string? GetCustomerContext()
        => _httpContextAccessor.HttpContext?.User?.FindFirst("tenant")?.Value;
}

// Registration (must be before AddTjiddeLogger):
services.AddSingleton<ICustomerContextAccessor, HttpCustomerContextAccessor>();
services.AddLogging(builder => builder.AddTjiddeLogger());
```

### Logging a normal message

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public void PlaceOrder(int orderId)
    {
        _logger.LogInformation("Placing order {OrderId}", orderId);
    }
}
```

Output:
```
2024-03-17: 14:22:01: [INFORMATION] Class=>OrderService: Client=>AcmeCorp: Placing order 42
```

### Logging an exception

```csharp
try
{
    // ...
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process order {OrderId}", orderId);
}
```

Output:
```
2024-03-17: 14:22:01: [ERROR] Class=>OrderService: Client=>AcmeCorp: Failed to process order 42 | Exception: [System.InvalidOperationException: Payment gateway timeout | StackTrace: at OrderService.ProcessPayment() in OrderService.cs:line 42] -> [System.TimeoutException: The operation timed out]
```

---

## Sinks: writing to more than the console

The console is the default destination. Extra destinations implement `ILogSink` (namespace `Tjidde.Logging.Sinks`) and receive every entry as an immutable `TjiddeLogEntry`: `Timestamp`, `Level`, `EventId`, `Category`, `ClassName`, `MethodName`, `Customer`, `Message`, `FormattedException`, `RenderedLine` (text or JSON, as on the console), `OutputFormat` and `IsMetrics`. All text in the entry is masked; it never contains unmasked values.

```csharp
builder.Logging
    .AddTjiddeLogger()
    .AddTjiddeInMemorySink(capacity: 500)   // built-in: inject InMemoryLogSink to read the entries
    .AddTjiddeSink<MyFileSink>();           // your own sink, created by the container

// or register directly:
builder.Services.AddSingleton<ILogSink, MyFileSink>();
```

```csharp
public sealed class MyFileSink : ILogSink
{
    public void Write(TjiddeLogEntry entry) { /* fast, thread-safe */ }
}
```

- Sinks are called **synchronously** on the logging thread, in registration order. Keep `Write` fast and thread-safe; queue slow I/O yourself.
- An exception in a sink is ignored: logging never throws, and the console and the other sinks still get the entry. An entry logged from inside a sink is not sent to the sinks again.
- A sink must not take `ILogger<T>` in its constructor (circular dependency).
- `InMemoryLogSink` keeps the newest `Capacity` entries (default 1000), with `GetSnapshot()`, `Count`, `Clear()` and the events `EntryAdded` and `Cleared`. Useful for UIs and tests.
- Set `WriteToConsole = false` (or `"TjiddeLogger": { "WriteToConsole": false }`) to write only to the sinks, for example in a desktop or terminal UI. It applies immediately when the options reload.

---

## Output Format

```
YYYY-MM-DD: HH:mm:ss: [LEVEL] Class=>ClassName Method=>MethodName: Client=>Customer: Message
```

- `LEVEL` is one of: `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL`, `METRICS`
- `METRICS` is shown for entries written with `_logger.LogMetrics(...)`. They are logged at `LogLevel.Information` with event `Metrics` (id 10000), so other providers such as `AddConsole()` see a normal information entry. For real application metrics, use `System.Diagnostics.Metrics`.
- `Method=>` is included when set via `using (_logger.BeginMethodScope())` (or a scope with key `MethodName`)
- `Client=>` is omitted when no customer context is active
- The timestamp is local time by default; set `UseUtcTimestamp = true` for UTC. JSON output writes `@timestamp` as ISO-8601 with the offset (for example `2026-10-02T10:15:00.0000000+00:00`)
- The clock is the `TimeProvider` registered in DI (for example a `FakeTimeProvider` in tests), or `TimeProvider.System` when none is registered

---

## Options Reference

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `WriteToConsole` | `bool` | `true` | Write entries to the console; `false` writes only to the registered sinks |
| `IncludeScopes` | `bool` | `true` | Include scope information in output |
| `UseUtcTimestamp` | `bool` | `false` | Write timestamps in UTC instead of local time |
| `ResolveMethodNameFromStackTrace` | `bool` | `false` | Fall back to the stack trace for the method name when no `MethodName` scope is active (slow) |
| `IncludeStackTrace` | `bool` | `true` | Include stack trace in exception output |
| `IncludeInnerExceptions` | `bool` | `true` | Include inner exception chain |
| `EnableSensitiveDataMasking` | `bool` | `true` | Apply sensitive data masking |
| `MaskPlaceholder` | `string` | `[REDACTED]` | Replacement text for masked values |
| `AdditionalSensitiveKeys` | `IList<string>` | `[]` | Extra keys to treat as sensitive |
| `MinimumLevel` *(obsolete)* | `LogLevel` | `Trace` | Use `Logging:Tjidde:LogLevel` instead; removed in 2.0 |
| `CategoryMinimumLevels` *(obsolete)* | `IDictionary<string, LogLevel>` | `{}` | Use `Logging:Tjidde:LogLevel` instead; removed in 2.0 |

---

## Sensitive Data Masking

The following field names and message patterns are masked by default:

`password`, `wachtwoord`, `token`, `access_token`, `refresh_token`, `secret`,
`client_secret`, `api_key`, `x-api-key`, `authorization`, `bearer`, `cookie`, `set-cookie`

Masking is applied to:
- Plain log message strings (pattern: `key=value`, `key: value`)
- Structured log property names
- Dictionary/state collections passed to the logger

### Runtime keys

Values that are only known at runtime (a token from a vault, a customer code) can be added while the app runs. They are masked as literal values anywhere in the output and as key names, and apply immediately to existing loggers.

- Recommended: `UseIsolatedMaskedKeys()` and inject `MaskedKeysStore` (`Add`, `Remove`, `Clear`, `GetKeys`). The keys belong to that host only.
- Convenience: the static `MaskedKeysContext.Add(...)` / `Remove(...)` / `Clear()`, used by default. These keys are process-wide.
- Custom: register your own `IMaskedKeysAccessor`. Keep `GetKeys()` cheap and thread-safe; returning an immutable snapshot is fastest.

### Security notes

> **Important:** Sensitive data masking significantly reduces the risk of leaking secrets in logs, but it is **not foolproof**. It cannot detect:
> - Secrets embedded in arbitrary string concatenations
> - Secrets in custom object `.ToString()` output
> - Obfuscated or encoded values
> - New field names not in the known list
>
> Always review log output when integrating new data sources. Do not rely solely on this package as a security boundary.

---

## Future Extensibility

The following capabilities are **intentionally not implemented** in v1.0 but the package is designed to support them:

- **Method argument logging** — The `ICustomerContextAccessor` and scope design allow future decorator/interceptor-based enrichment. Attribute-based exclusion (`[SensitiveArgument]`) can be layered on top.
- **Additional structured outputs** — JSON is built in; OTLP/log-record output can be added as an alternative formatter or sink.
- **Automatic argument logging** — Explicitly deferred due to privacy, security, and performance risks. When added, it will require opt-in per method or argument.

---

## Architecture

```
Tjidde.Logging/
├── Context/
│   ├── ICustomerContextAccessor.cs       # Abstraction for customer context
│   ├── AsyncLocalCustomerContextAccessor.cs  # Default AsyncLocal implementation
│   └── CustomerContext.cs                # Static AsyncLocal store
├── Extensions/
│   └── TjiddeLoggingBuilderExtensions.cs  # AddTjiddeLogger() extension methods
├── Formatting/
│   ├── IExceptionFormatter.cs            # Exception formatting contract
│   └── ExceptionFormatter.cs            # Structured exception output
├── Logging/
│   ├── ConsoleLogProcessor.cs          # Background console writer (default sink)
│   ├── TjiddeLogger.cs                 # ILogger implementation
│   └── TjiddeLoggerProvider.cs         # ILoggerProvider implementation
├── Sinks/
│   ├── ILogSink.cs                       # Extra destinations for entries
│   ├── TjiddeLogEntry.cs                 # Immutable, masked entry passed to sinks
│   └── InMemoryLogSink.cs                # Built-in bounded in-memory sink
├── Masking/
│   ├── IMaskedKeysAccessor.cs            # Runtime keys abstraction (+ GlobalMaskedKeysAccessor)
│   ├── MaskedKeysStore.cs                # Per-host runtime keys (UseIsolatedMaskedKeys)
│   ├── MaskedKeysContext.cs              # Static, process-wide runtime keys
│   ├── ISensitiveDataMasker.cs           # Masking contract
│   └── SensitiveDataMasker.cs           # Default masking implementation
└── Options/
    └── TjiddeLoggerOptions.cs          # Configuration options
```

---

## License

Licensed under the [Apache License 2.0](LICENSE). Copyright © 2026 Tjidde Nieuwenhuizen.

If you redistribute this package or a modified version of it, keep the [NOTICE](NOTICE) file, as the license requires.
