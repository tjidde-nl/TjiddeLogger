# Tjidde.Logging — Wiki (English)

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Quick Start](#quick-start)
4. [Log Output Format](#log-output-format)
5. [Configuration Options](#configuration-options)
6. [Customer Context](#customer-context)
7. [Sensitive Data Masking](#sensitive-data-masking)
8. [Exception Formatting](#exception-formatting)
9. [Scopes](#scopes)
10. [Metrics Entries](#metrics-entries)
11. [CI/CD (GitHub Actions)](#cicd-github-actions)
12. [Future Work](#future-work)

---

## Introduction

**Tjidde.Logging** is a structured, opinionated logging library built on top of `Microsoft.Extensions.Logging`. It produces consistent, single-line log output that is easy to read in consoles and log aggregators. Key features:

- Standardised log format with timestamp, log level, class name, method name, and customer context.
- Automatic masking of sensitive data (passwords, tokens, API keys, etc.).
- Compact single-line exception formatting including inner exceptions and stack traces.
- Customer context propagation via `AsyncLocal` — safe for async/await and multi-tenant scenarios.
- Scope support for structured logging.
- `LogMetrics` for metric-style entries, shown as `[METRICS]` alongside regular log output.
- Targets .NET 7, 8, 9, and 10.

---

## Installation

Install the NuGet package:

```bash
dotnet add package Tjidde.Logging
```

Or add it manually to your `.csproj`:

```xml
<PackageReference Include="Tjidde.Logging" Version="1.0.1" />
```

---

## Quick Start

### ASP.NET Core / Generic Host

In `Program.cs`, clear the default providers and add the Tjidde logger:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeScopes = true;
});

// Easily switch between Text and Json formats:
builder.Logging.UseTjiddeJsonFormat(); // Switch to JSON
// builder.Logging.UseTjiddeTextFormat(); // Switch back to Text (default)

var app = builder.Build();
app.Run();
```

### Console / Worker Service

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddTjiddeLogger();
    })
    .Build();

await host.RunAsync();
```

### Injecting and using the logger

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
    }

    public void PlaceOrder(int orderId)
    {
        _logger.LogInformation("Order {OrderId} placed successfully", orderId);
    }
}
```

---

## Log Output Format

Every log entry is written as a **single line** in the following format:

```
YYYY-MM-DD: HH:mm:ss: [LEVEL] CS=>ClassName Method=>MethodName: Client=>Customer: Message [Masked: key1, key2] | Exception: [...] | Scopes: scope1 > scope2
```

### Example output

```
2026-03-18: 09:15:41: [INFORMATION] CS=>OrderService Method=>PlaceOrder: Client=>AcmeCorp: Order 42 placed successfully
2026-03-18: 09:15:42: [ERROR] CS=>PaymentService Method=>ProcessPayment: Client=>AcmeCorp: Payment failed | Exception: [System.InvalidOperationException: Gateway timeout]
2026-03-18: 09:15:43: [WARNING] CS=>AuthService Method=>Login: Login with password [REDACTED]
```

### Log levels

| Level | Label |
|---|---|
| Trace | `[TRACE]` |
| Debug | `[DEBUG]` |
| Information | `[INFORMATION]` |
| Warning | `[WARNING]` |
| Error | `[ERROR]` |
| Critical | `[CRITICAL]` |
| Metrics *(Information + event `Metrics`)* | `[METRICS]` |

### Console colours

Each log level is printed in a distinct console colour for quick visual scanning:

| Level | Colour |
|---|---|
| Trace | Gray |
| Debug | Cyan |
| Information | Green |
| Warning | Yellow |
| Error | Red |
| Critical | Dark Red |
| Metrics *(Information + event `Metrics`)* | Magenta |

---

## Configuration Options

You can configure Tjidde logger either in code or directly from `appsettings.json`.

### Option 1: `appsettings.json` (no options lambda)

```json
{
  "TjiddeLogger": {
    "OutputFormat": "Json",
    "EnableOpenTelemetryExport": true,
    "OpenTelemetryActivitySourceName": "MyCompany.MyApp",
    "OpenTelemetryCreateFallbackActivity": false,
    "IncludeScopes": true,
    "IncludeStackTrace": true,
    "IncludeInnerExceptions": true,
    "EnableSensitiveDataMasking": true,
    "MaskPlaceholder": "[REDACTED]",
    "AdditionalSensitiveKeys": ["tenantKey", "internalCode"]
  }
}
```

```csharp
builder.Logging.AddTjiddeLogger(builder.Configuration);
// or bind an explicit section:
builder.Logging.AddTjiddeLogger(builder.Configuration.GetSection("TjiddeLogger"));
```

Log levels are not part of the `TjiddeLogger` section: configure them under `Logging`, see [Log levels per category](#log-levels-per-category).

### Log levels per category

Use the standard `Logging` section of `appsettings.json`. Tjidde.Logging's provider alias is `Tjidde`, so `Logging:Tjidde:LogLevel` applies to Tjidde.Logging only, while `Logging:LogLevel` applies to every provider. For Tjidde.Logging, a rule under `Logging:Tjidde:LogLevel` takes precedence over one under `Logging:LogLevel`. Keys are categories or namespace prefixes (the longest match wins) and `Default` is the fallback:

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

The same in code:

```csharp
builder.Logging.AddFilter<TjiddeLoggerProvider>("MyCompany.MyApp.Services.OrderService", LogLevel.Debug);
```

These filters are reloaded with the configuration, like the other options.

**Obsolete: `MinimumLevel` and `CategoryMinimumLevels`.** `TjiddeLogger:MinimumLevel` and `TjiddeLogger:CategoryMinimumLevels` duplicate the filters above and will be removed in 2.0. Until then they still work and are still bound from configuration. They run *after* the `Logging` filters, so an entry must pass both and the stricter level wins:

| `Logging:Tjidde:LogLevel:Default` | `TjiddeLogger:MinimumLevel` | Lowest level written |
|---|---|---|
| `Warning` | `Debug` | `Warning` |
| `Debug` | `Warning` | `Warning` |
| `Debug` | *(not set, `Trace`)* | `Debug` |
| *(not set, and no `Logging:LogLevel` rule: framework default `Information`)* | `Debug` | `Information` |

The last row is a common surprise: without a `Logging` rule, the framework's default minimum is `Information`, so `MinimumLevel = Debug` alone never shows debug entries. Move the values to `Logging:Tjidde:LogLevel` (keys and `Default` work the same way).

### Dynamic Censoring (at runtime)

You can add extra sensitive words for censoring at runtime from within your application using `MaskedKeysContext`. This is useful for redacting data that is only known at execution time.

```csharp
using Tjidde.Logging.Masking;

// Redact a specific value globally
MaskedKeysContext.Add("SuperSecretToken", "PersonalID");

// The logger will now automatically redact these words from all future log entries
_logger.LogInformation("Processing token SuperSecretToken for user PersonalID");
// Output: Processing token [REDACTED] for user [REDACTED]

// You can also remove keys if they are no longer sensitive
MaskedKeysContext.Remove("PersonalID");
```

### Option 2: code-based options

Pass an `Action<TjiddeLoggerOptions>` delegate to `AddTjiddeLogger` to customise behaviour:

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeScopes              = true;          // Include scope info in output
    options.UseUtcTimestamp            = false;         // true: timestamps in UTC instead of local time
    options.IncludeStackTrace          = true;          // Include stack trace in exception output
    options.IncludeInnerExceptions     = true;          // Include inner exceptions
    options.EnableSensitiveDataMasking = true;          // Mask sensitive values
    options.MaskPlaceholder            = "[REDACTED]";  // Replacement text for masked values
    options.AdditionalSensitiveKeys    = ["tenantKey", "internalCode"]; // Extra keys to mask
});
```

### All options

| Option | Type | Default | Description |
|---|---|---|---|
| `OutputFormat` | `TjiddeLogOutputFormat` | `Text` | Log rendering mode: `Text` or `Json` (Elasticsearch-friendly JSON line). |
| `MinimumLevel` *(obsolete)* | `LogLevel` | `Trace` | Global minimum log level fallback when no category override matches. Use `Logging:Tjidde:LogLevel` instead; removed in 2.0. |
| `CategoryMinimumLevels` *(obsolete)* | `IDictionary<string, LogLevel>` | `{}` | Category/namespace/class-specific minimum levels. Use `Logging:Tjidde:LogLevel` instead; removed in 2.0. |
| `EnableOpenTelemetryExport` | `bool` | `false` | Emits each log as an OpenTelemetry event on the current `Activity` (for OTEL pipelines). |
| `OpenTelemetryActivitySourceName` | `string` | `Tjidde.Logging` | Activity source name used for optional fallback activity creation. |
| `OpenTelemetryCreateFallbackActivity` | `bool` | `false` | Creates a short-lived internal activity if no current `Activity` exists. |
| `IncludeScopes` | `bool` | `true` | Appends active scope values to the log line. |
| `UseUtcTimestamp` | `bool` | `false` | Writes timestamps in UTC instead of local time. See [Timestamps and `TimeProvider`](#timestamps-and-timeprovider). |
| `ResolveMethodNameFromStackTrace` | `bool` | `false` | Falls back to the stack trace for the method name when no `MethodName` scope is active. Walks the stack on every log call, so it is slow; prefer `BeginMethodScope()`. |
| `IncludeStackTrace` | `bool` | `true` | Includes the stack trace when an exception is logged. |
| `IncludeInnerExceptions` | `bool` | `true` | Includes inner exceptions in the formatted output. |
| `EnableSensitiveDataMasking` | `bool` | `true` | Enables automatic masking of sensitive values. |
| `MaskPlaceholder` | `string` | `[REDACTED]` | The text used to replace masked values. |
| `AdditionalSensitiveKeys` | `IList<string>` | `[]` | Extra property names to treat as sensitive. |

### JSON output mode (`OutputFormat = Json`)

When `OutputFormat` is set to `Json`, each log entry is written as one JSON object (single-line) with stable fields intended for ingestion by Elasticsearch and other log platforms.

Example shape:

```json
{
  "@timestamp": "2026-03-23T15:00:00.0000000+01:00",
  "message": "Order placed",
  "level": "INFORMATION",
  "category": "MyCompany.MyApp.Services.OrderService",
  "class": "OrderService",
  "eventId": 42,
  "eventName": "OrderCreated",
  "customer": "AcmeCorp",
  "method": "CreateOrder",
  "scopes": ["RequestId=123"],
  "maskedFields": ["password"],
  "exception": null,
  "properties": {
    "OrderId": 1001,
    "Amount": 29.95
  }
}
```

### Timestamps and `TimeProvider`

Timestamps are local time by default. Set `UseUtcTimestamp` to write UTC instead, which is usually what you want when logs from servers in different time zones end up in one place:

```json
{
  "TjiddeLogger": {
    "UseUtcTimestamp": true
  }
}
```

- Text output keeps the `yyyy-MM-dd: HH:mm:ss` format without an offset, so check `UseUtcTimestamp` when you read it.
- JSON output writes `@timestamp` as ISO-8601 with the offset: `2026-10-02T10:15:00.0000000+00:00` with `UseUtcTimestamp`, the local offset (for example `+02:00`) without it.

The clock is a `System.TimeProvider`. When one is registered in DI, Tjidde.Logging uses it; otherwise it uses `TimeProvider.System`. In tests you can register a `FakeTimeProvider` (package `Microsoft.Extensions.TimeProvider.Testing`) to get predictable timestamps:

```csharp
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 10, 15, 0, TimeSpan.Zero));
services.AddSingleton<TimeProvider>(clock);
services.AddLogging(logging => logging.AddTjiddeLogger(options => options.UseUtcTimestamp = true));
```

Without DI, pass it to the constructor: `new TjiddeLoggerProvider(optionsMonitor, customerContextAccessor, maskedKeysAccessor, clock)`.

### OpenTelemetry integration

Enable OpenTelemetry export to attach each Tjidde log entry as an event to the active trace `Activity`.

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.EnableOpenTelemetryExport = true;
    options.OpenTelemetryActivitySourceName = "MyCompany.MyApp";
    options.OpenTelemetryCreateFallbackActivity = false;
});
```

Event tags include fields such as `log.level`, `log.message`, `log.category`, `event.id`, `event.name`, `code.function`, and exception details.

---

## Customer Context

The customer context allows you to tag every log entry with a customer or tenant identifier without passing it through every method call. It uses `AsyncLocal<T>` so it flows correctly through `async`/`await` chains.

### Setting the context

```csharp
// At the start of a request, middleware, or background job:
CustomerContext.Set("AcmeCorp");

// All log entries from this point forward will include "AcmeCorp:" in the output.
_logger.LogInformation("Processing started");
// → 2026-03-18: 09:00:00: [INFORMATION] MyService: AcmeCorp: Processing started
```

### Clearing the context

```csharp
CustomerContext.Clear();
```

### Reading the current value

```csharp
var current = CustomerContext.Current; // returns null if not set
```

### Typical usage in ASP.NET Core middleware

```csharp
public class CustomerContextMiddleware
{
    private readonly RequestDelegate _next;

    public CustomerContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var customerId = context.User?.FindFirst("customer_id")?.Value;
        CustomerContext.Set(customerId);
        try
        {
            await _next(context);
        }
        finally
        {
            CustomerContext.Clear();
        }
    }
}
```

---

## Sensitive Data Masking

Masking is enabled by default. The masker scans both the formatted message string and structured log properties.

### Built-in sensitive keys

The following keys are masked automatically (case-insensitive, ignoring `-` and `_`):

`password`, `wachtwoord`, `token`, `accesstoken`, `access_token`, `refreshtoken`, `refresh_token`, `secret`, `clientsecret`, `client_secret`, `apikey`, `api_key`, `x-api-key`, `authorization`, `bearer`, `cookie`, `set-cookie`

### Supported message patterns

| Pattern | Example input | Result |
|---|---|---|
| `key=value` | `password=hunter2` | `password=[REDACTED]` |
| `key: value` | `token: abc123` | `token: [REDACTED]` |
| `"key": "value"` (JSON; quoted values are masked up to the closing quote) | `"password": "my secret"` | `"password": "[REDACTED]"` |
| HTTP auth scheme (the scheme stays visible) | `Authorization: Bearer abc.def` | `Authorization: Bearer [REDACTED]` |
| `key value` (value of 4 or more characters) | `password hunter2` | `password [REDACTED]` |

> **Note:** Short words after a key, as in `"password is missing"`, are **not** masked. Longer words are: `Refreshing token cache` becomes `Refreshing token [REDACTED]`.
>
> If masking a very large message takes too long, the whole message is replaced by the placeholder instead of being written unmasked.

### Adding custom sensitive keys

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.AdditionalSensitiveKeys = ["tenantKey", "internalCode", "ssn"];
});
```

### Custom placeholder

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.MaskPlaceholder = "***";
});
```

### Disabling masking

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.EnableSensitiveDataMasking = false;
});
```

---

## Exception Formatting

Exceptions are formatted inline on the same log line in a compact, readable format:

```
| Exception: [System.InvalidOperationException: Something went wrong | StackTrace: at MyApp.Service.DoWork() ...] -> [System.ArgumentNullException: Value cannot be null]
```

- Each exception is wrapped in `[ExceptionType: Message | StackTrace: ...]`.
- Inner exceptions are appended with ` -> [...]`.
- Stack trace frames are separated by ` | `.

### Controlling exception output

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeStackTrace      = false; // omit stack trace
    options.IncludeInnerExceptions = false; // omit inner exceptions
});
```

---

## Scopes

Scopes allow you to attach contextual key-value pairs or strings to a group of log entries.

### Using scopes

```csharp
using (_logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = "req-001" }))
{
    _logger.LogInformation("Handling request");
    // → ... | Scopes: RequestId=req-001
}
```

### Method name scope

The logger automatically extracts a `MethodName` key from the active scope and places it directly after the class name in the log line (not in the Scopes section). `BeginMethodScope()` fills in the calling method name at compile time, so it has no runtime cost:

```csharp
public void PlaceOrder()
{
    using (_logger.BeginMethodScope())
    {
        _logger.LogInformation("Order placed");
        // → 2026-03-18: 09:00:00: [INFORMATION] Class=>OrderService Method=>PlaceOrder: Order placed
    }
}
```

This is equivalent to `_logger.BeginScope(new Dictionary<string, object> { ["MethodName"] = "PlaceOrder" })`.

Without a `MethodName` scope the method name is omitted. Set `ResolveMethodNameFromStackTrace = true` to derive it from the stack trace instead; this walks the stack on every log call, so avoid it on hot paths.

### Disabling scopes

```csharp
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeScopes = false;
});
```

---

## Metrics Entries

Tjidde.Logging provides `LogMetrics` for writing metric-style entries (counters, timings, gauges) alongside regular log output. A metrics entry is a normal `LogLevel.Information` entry with the event ID `MetricsLoggerExtensions.MetricsEventId` (`Id = 10000`, `Name = "Metrics"`). Tjidde.Logging recognizes that event and shows the entry as `[METRICS]`; other providers (for example `AddConsole()`) see an ordinary information entry.

> For real application metrics (dashboards, alerting, aggregation), use [`System.Diagnostics.Metrics`](https://learn.microsoft.com/dotnet/core/diagnostics/metrics) with OpenTelemetry or `dotnet-counters`. `LogMetrics` is meant for metric values you also want to see in the log.

### Using LogMetrics

Add the using directive and call `LogMetrics`:

```csharp
using Tjidde.Logging.Extensions;

// Simple message
_logger.LogMetrics("requests_total=42");

// With format arguments
_logger.LogMetrics("response_time_ms={ResponseTime}", elapsed.TotalMilliseconds);

// With an exception
_logger.LogMetrics(ex, "payment_failures_total={Count}", failureCount);

// With an event ID: an unnamed event ID gets the name "Metrics" and is shown as [METRICS];
// an event ID with another name is passed on unchanged and shown as [INFORMATION].
_logger.LogMetrics(new EventId(200), "throughput_rps={Rps}", rps);
```

### Example output

```
2026-03-18: 11:00:00: [METRICS] OrderService ProcessOrder: AcmeCorp: requests_total=42
```

The `[METRICS]` label is printed in **Magenta** in the console for easy visual distinction.

### Filtering

Tjidde.Logging's own (obsolete) `MinimumLevel` and `CategoryMinimumLevels` never drop metrics entries. Filters of `Microsoft.Extensions.Logging` itself (for example `Logging:LogLevel:Default` or `Logging:Tjidde:LogLevel:Default` in `appsettings.json`) treat them as `Information`, so a category filtered to `Warning` or higher also filters its metrics entries.

### The obsolete `Metrics` log level

Earlier versions logged metrics at the custom level `MetricsLoggerExtensions.Metrics` (`(LogLevel)10`). That value is not a valid `LogLevel`: other providers reject it, and the Microsoft console formatters throw an `ArgumentOutOfRangeException`, so `LogMetrics` crashed as soon as `AddConsole()` was also registered. The field is now marked `[Obsolete]`. Tjidde.Logging still shows `(LogLevel)10` as `[METRICS]`, but use `LogMetrics(...)` (or `MetricsEventId`) instead.

---

## CI/CD (GitHub Actions)

Two workflows live in `.github/workflows/`:

| Workflow | Runs on | What it does |
|---|---|---|
| `ci.yml` | Every push to `main` and every pull request | Builds the solution, runs the tests, packs the NuGet package and uploads it as a build artifact. |
| `publish.yml` | Pushing a version tag such as `v1.2.3` | Builds and tests with the version from the tag, then publishes the package and its symbols package (`.snupkg`) to nuget.org. |

Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing). GitHub proves to nuget.org which repository and workflow is publishing, and receives an API key that is valid for one hour. No long-lived API key is stored anywhere.

### One-time setup

1. On nuget.org, open your account menu, choose **Trusted Publishing** and add a policy:
   - **Repository Owner:** `tjidde-nl`
   - **Repository:** `TjiddeLogger`
   - **Workflow File:** `publish.yml`
   - **Environment:** leave empty

   If the form asks for scopes, allow both new packages and new versions (for example with the glob `Tjidde.*`): the first release creates the `Tjidde.Logging` package.
2. In the GitHub repository, go to **Settings → Secrets and variables → Actions** and add a repository secret `NUGET_USER` with your nuget.org username (your profile name, not your email address).

For a private repository the policy is first active for 7 days only. It becomes permanent after the first successful publish.

### Releasing a version

First add a `## [1.0.4] - <date>` section to `CHANGELOG.md` and commit it. The changelog becomes the package's release notes on nuget.org, and the publish workflow stops if the tagged version has no section. Then tag and push:

```bash
git tag v1.0.4
git push origin v1.0.4
```

The tag sets the package version: `v1.0.4` publishes `1.0.4`, and pre-releases such as `v1.1.0-beta.1` work too. The `<Version>` in the `.csproj` only applies to local builds. Publishing a version that already exists on nuget.org is skipped instead of failing.

---

## Future Work

The following improvements and features are planned or considered for future releases:

- **File sink** — Write log output to rolling log files in addition to the console.
- **Redaction audit log** — Optionally emit a separate audit entry listing which fields were redacted, for compliance scenarios.
- **Custom sensitive key providers** — Allow injecting `ISensitiveKeyProvider` implementations so keys can be loaded from configuration or a secrets store at runtime.
- **NuGet package signing** — Sign the NuGet package in the pipeline for supply-chain security.
- **Blazor / MAUI sample** — Extend the demo app to show real-time log streaming in a Blazor UI or MAUI desktop app.
