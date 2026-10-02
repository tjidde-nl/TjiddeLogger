# Tjidde.Logging: integration guide for AI coding assistants

> This guide tells an AI coding assistant (and anyone else) how to add Tjidde.Logging to a .NET application correctly.
> It ships inside the NuGet package and describes the version it came with.
> Follow **Integration steps** first, then apply **Rules for writing log statements** to all logging code you write.

## What the package does

Tjidde.Logging is a logging provider for `Microsoft.Extensions.Logging`. Application code keeps using `ILogger<T>`. The provider writes every log entry as one line to the console (stdout), as readable text or as JSON, and adds:

- the class name and, optionally, the method name of the caller
- a customer or tenant identifier from an ambient context
- masking of sensitive values (passwords, tokens, API keys, and so on)
- compact single-line exception formatting
- optional OpenTelemetry span events

Target frameworks: `net7.0`, `net8.0`, `net9.0`, `net10.0`.

| Namespace | Contains |
|---|---|
| `Tjidde.Logging.Extensions` | `AddTjiddeLogger`, `UseTjiddeJsonFormat`, `UseTjiddeTextFormat`, `BeginMethodScope`, `LogMetrics` |
| `Tjidde.Logging.Options` | `TjiddeLoggerOptions`, `TjiddeLogOutputFormat` |
| `Tjidde.Logging.Context` | `CustomerContext`, `ICustomerContextAccessor` |
| `Tjidde.Logging.Masking` | `MaskedKeysContext` |

## Integration steps

### 1. Install

```bash
dotnet add package Tjidde.Logging
```

### 2. Register the provider

ASP.NET Core:

```csharp
using Tjidde.Logging.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();                       // otherwise the default console logger prints every entry twice
builder.Logging.AddTjiddeLogger(builder.Configuration); // binds the "TjiddeLogger" section of appsettings.json

var app = builder.Build();
```

Worker service or other generic host:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddTjiddeLogger(builder.Configuration);
builder.Build().Run();
```

Console app without a host. Dispose the factory so pending lines are flushed:

```csharp
using var loggerFactory = LoggerFactory.Create(logging => logging.AddTjiddeLogger());
var logger = loggerFactory.CreateLogger<Program>();
logger.LogInformation("Started");
```

To configure in code instead of (or on top of) `appsettings.json`:

```csharp
using Tjidde.Logging.Options;

builder.Logging.AddTjiddeLogger(options =>
{
    options.OutputFormat = TjiddeLogOutputFormat.Json;
    options.AdditionalSensitiveKeys.Add("Iban");
});
```

`builder.Logging.UseTjiddeJsonFormat()` and `UseTjiddeTextFormat()` are shortcuts for the output format. Calling `AddTjiddeLogger` more than once is safe: the provider is registered once and all option delegates are applied in order.

### 3. Configure log levels

Two filters apply, and an entry must pass both:

1. The standard `Logging` section: `Logging:LogLevel`, or `Logging:Tjidde:LogLevel` for this provider only. Without configuration the minimum is `Information`, so `Debug` and `Trace` entries are dropped here unless you lower it.
2. `TjiddeLogger:MinimumLevel` and `TjiddeLogger:CategoryMinimumLevels`. The default is `Trace`, so this filter passes everything unless you set it.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "TjiddeLogger": {
    "OutputFormat": "Text",
    "IncludeScopes": true,
    "EnableSensitiveDataMasking": true,
    "AdditionalSensitiveKeys": [ "Iban", "Bsn" ]
  }
}
```

Options are read once per log category, when the first logger for that category is created. Changing configuration while the app runs has no effect; restart the app.

### 4. Set the customer context (optional)

Each entry can carry a customer or tenant id: `Client=>…` in text output, `customer` in JSON. Set it at the start of every request:

```csharp
using Tjidde.Logging.Context;

app.UseAuthentication();
app.Use(async (context, next) =>
{
    CustomerContext.Set(context.User.FindFirst("customer_id")?.Value);
    try
    {
        await next(context);
    }
    finally
    {
        CustomerContext.Clear();
    }
});
```

`CustomerContext` is stored in an `AsyncLocal`, so the value flows through `await` calls within that request only. In background jobs, call `CustomerContext.Set(...)` at the start of each unit of work.

Alternatively, supply the customer id yourself by implementing `ICustomerContextAccessor`. Register it as a **singleton** (the provider is a singleton and calls it for every entry) and keep it thread-safe:

```csharp
using Tjidde.Logging.Context;

public sealed class ClaimsCustomerContextAccessor : ICustomerContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsCustomerContextAccessor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string? GetCustomerContext()
        => _httpContextAccessor.HttpContext?.User.FindFirst("customer_id")?.Value;
}
```

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICustomerContextAccessor, ClaimsCustomerContextAccessor>();
```

## Rules for writing log statements

1. **Use message templates with named placeholders, never string interpolation.** Masking of structured values depends on the placeholder names.

   ```csharp
   _logger.LogInformation("Order {OrderId} placed for {CustomerEmail}", orderId, email); // correct
   _logger.LogInformation($"Order {orderId} placed for {email}");                         // wrong
   ```

2. **Name placeholders that can hold secrets after a sensitive key**, such as `{Password}`, `{ApiKey}`, `{AccessToken}`, `{ClientSecret}`, `{Authorization}` or `{Cookie}`. Matching is on the exact name, case-insensitive and ignoring `-` and `_`: `{Password}` is masked, `{UserPassword}` is not. Add domain-specific names to `AdditionalSensitiveKeys`.

3. **Pass credentials and headers as named properties, not in the message text.** A property such as `{Authorization}` is masked as a whole. Free text is only masked by patterns: `Authorization: Bearer abc` becomes `Authorization: Bearer [REDACTED]`, but a header with several values, such as `Cookie: a=1; b=2`, is only partly masked. Masking is a safety net: do not log secrets on purpose.

4. **Add the method name with `BeginMethodScope()`.** The compiler fills in the name, so it costs nothing at runtime. It requires `IncludeScopes` (on by default).

   ```csharp
   using Tjidde.Logging.Extensions;

   public sealed class OrderService
   {
       private readonly ILogger<OrderService> _logger;

       public OrderService(ILogger<OrderService> logger) => _logger = logger;

       public async Task PlaceOrderAsync(Order order)
       {
           using var scope = _logger.BeginMethodScope(); // adds Method=>PlaceOrderAsync
           _logger.LogInformation("Placing order {OrderId}", order.Id);
           await Task.CompletedTask;
       }
   }
   ```

   Do not enable `ResolveMethodNameFromStackTrace`: it walks the stack on every log call.

5. **Use `LogMetrics` for metric-style entries.** They are written at `LogLevel.Information` with event `Metrics` (id 10000) and shown as `METRICS`; Tjidde's own minimum level never drops them, and other providers such as `AddConsole()` see a normal information entry. Never log at `(LogLevel)10` / `MetricsLoggerExtensions.Metrics` (obsolete: other providers throw on it). For real metrics, use `System.Diagnostics.Metrics`.

   ```csharp
   _logger.LogMetrics("checkout_duration_ms={DurationMs}", elapsedMs);
   ```

6. **Pass exceptions as the first argument.** They are formatted on one line, including inner exceptions, and masked.

   ```csharp
   _logger.LogError(ex, "Payment {PaymentId} failed", paymentId);
   ```

## Sensitive data masking

Masking is on by default (`EnableSensitiveDataMasking`).

| What | How it is masked |
|---|---|
| Structured properties whose name is a sensitive key | The value becomes `MaskPlaceholder` (default `[REDACTED]`) in the message, in JSON `properties` and in OpenTelemetry tags. The name is listed in `[Masked: …]` or `maskedFields`. |
| Message text, exceptions, scopes and other string values | Pattern-based, for every sensitive key: `password=x`, `password: x`, `"password": "x"` and `password x` (a value of 4 or more characters). Quoted values are masked up to the closing quote, and an HTTP auth scheme stays visible: `Authorization: Bearer [REDACTED]`. |

Built-in sensitive keys: `password`, `wachtwoord`, `token`, `accesstoken`, `access_token`, `refreshtoken`, `refresh_token`, `secret`, `clientsecret`, `client_secret`, `apikey`, `api_key`, `x-api-key`, `authorization`, `bearer`, `cookie`, `set-cookie`.

The pattern masking also masks the word after a sensitive key, so `Refreshing token cache` is written as `Refreshing token [REDACTED]`. Reword such messages if that matters.

To mask values that are only known at runtime, use `MaskedKeysContext.Add(...)`. It masks the literal value anywhere in the output and also treats it as a sensitive key name. The change applies immediately to all loggers, including existing ones; `Remove(...)` and `Clear()` undo it.

```csharp
using Tjidde.Logging.Masking;

MaskedKeysContext.Add(apiKeyFromVault);
```

If masking a very large message takes too long, the whole message is replaced by the placeholder instead of being written unmasked.

## Output formats

Text (default), one line per entry:

```
2026-10-01: 12:14:00: [INFORMATION] Class=>OrderService Method=>PlaceOrderAsync: Client=>AcmeCorp: Placing order 42
```

Full shape, where segments without a value are left out:

```
yyyy-MM-dd: HH:mm:ss: [LEVEL] Class=>ClassName Method=>MethodName: Client=>Customer: Message [Masked: Key1, Key2] | Exception: … | Scopes: a > b
```

Levels: `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL`, `METRICS` (`METRICS` = an `Information` entry with event name `Metrics`). Timestamps are local time. Each level gets its own color unless the output is redirected.

JSON (`OutputFormat: Json`), meant for log shippers such as Elasticsearch or Loki. Shown formatted here; the real output is one object per line, with standard JSON escaping (`+` can appear as `+`):

```json
{
  "@timestamp": "2026-10-01T12:14:00.4826262+02:00",
  "message": "Placing order 42",
  "level": "INFORMATION",
  "category": "Shop.Orders.OrderService",
  "class": "OrderService",
  "eventId": 0,
  "eventName": null,
  "customer": "AcmeCorp",
  "method": "PlaceOrderAsync",
  "scopes": null,
  "maskedFields": null,
  "exception": null,
  "properties": { "OrderId": 42 }
}
```

## OpenTelemetry

With `EnableOpenTelemetryExport = true`, each entry is added as an event named `log` to `Activity.Current` (the active span). Tags include `log.level`, `log.message`, `log.category`, `event.id`, `enduser.id` (the customer), `code.function`, `exception.*` and `log.property.<Name>` (masked). This does not export OpenTelemetry log records (OTLP logs); use OpenTelemetry's own logging provider for that.

Without an active span nothing is exported, unless `OpenTelemetryCreateFallbackActivity = true`. Then a short-lived activity named `tjidde.log` is started on the source `OpenTelemetryActivitySourceName` (default `Tjidde.Logging`). Add that source to your tracer, otherwise the activity is never created (requires the `OpenTelemetry.Extensions.Hosting` package):

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Tjidde.Logging"));
```

## Shutdown and flushing

A background thread writes the lines to the console, so log calls do not wait for console I/O. Pending lines are flushed when the host stops, when a `LoggerFactory` is disposed, and on normal process exit. Lines still queued when the process crashes can be lost.

## Testing code that uses it

In unit tests, inject `NullLogger<T>.Instance` (from `Microsoft.Extensions.Logging.Abstractions`) instead of the Tjidde logger. If a test must read the console output, dispose the `LoggerFactory` first so all lines have been written.

## Options reference

Section name in `appsettings.json`: `TjiddeLogger`.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `OutputFormat` | `TjiddeLogOutputFormat` | `Text` | `Text` or `Json`. |
| `MinimumLevel` | `LogLevel` | `Trace` | Minimum level when no category rule matches. |
| `CategoryMinimumLevels` | `IDictionary<string, LogLevel>` | empty | Minimum level per category or namespace prefix; the key `Default` is the fallback. |
| `IncludeScopes` | `bool` | `true` | Append scope values to each entry. Also required for `BeginMethodScope`. |
| `ResolveMethodNameFromStackTrace` | `bool` | `false` | Find the method name from the stack trace when no method scope is active. Slow. |
| `IncludeStackTrace` | `bool` | `true` | Include stack traces in exception output. |
| `IncludeInnerExceptions` | `bool` | `true` | Include inner exceptions in exception output. |
| `EnableSensitiveDataMasking` | `bool` | `true` | Mask sensitive values. |
| `MaskPlaceholder` | `string` | `[REDACTED]` | Replacement text for masked values. |
| `AdditionalSensitiveKeys` | `IList<string>` | empty | Extra sensitive key names (exact match, case-insensitive, ignoring `-` and `_`). |
| `EnableOpenTelemetryExport` | `bool` | `false` | Add each entry as an event to the active span. |
| `OpenTelemetryActivitySourceName` | `string` | `Tjidde.Logging` | Source for the fallback activity. |
| `OpenTelemetryCreateFallbackActivity` | `bool` | `false` | Start a short-lived activity when no span is active. |

## Checklist

- [ ] `ClearProviders()` is called before `AddTjiddeLogger(...)`.
- [ ] `Logging:LogLevel` is low enough for the levels the app needs.
- [ ] The customer context is set per request or job, if used.
- [ ] Log calls use message templates with named placeholders, never `$"..."`.
- [ ] Placeholders that can hold secrets are named after a sensitive key, or the name is added to `AdditionalSensitiveKeys`.
- [ ] Credentials and headers are passed as named properties, not written into the message text.
- [ ] The host or `LoggerFactory` is disposed on shutdown.
