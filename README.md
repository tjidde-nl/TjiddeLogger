# Tjidde.Logging

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
<PackageReference Include="Tjidde.Logging" Version="1.0.0" />
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

### Setting customer context

Use `CustomerContext.Set(...)` at the start of a request, job, or operation. It flows through the async call chain automatically.

```csharp
// In middleware, a job handler, or service entry point:
CustomerContext.Set("AcmeCorp");

// Later in any service in the same async flow:
_logger.LogInformation("Order placed successfully.");
// Output: 2024-03-17: 14:22:01: [INF] OrderService: AcmeCorp: Order placed successfully.

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
2024-03-17: 14:22:01: [INF] OrderService: AcmeCorp: Placing order 42
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
2024-03-17: 14:22:01: [ERR] OrderService: AcmeCorp: Failed to process order 42
Exception Type : System.InvalidOperationException
Message        : Payment gateway timeout
Stack Trace    :
  at OrderService.ProcessPayment() in OrderService.cs:line 42
Inner Exception:
  -> Exception Type : System.TimeoutException
  -> Message        : The operation timed out
```

---

## Output Format

```
YYYY-MM-DD: HH:mm:ss: [LEVEL] Class=>ClassName Method=>MethodName: Client=>Customer: Message
```

- `LEVEL` is one of: `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL`, `METRICS`
- `METRICS` is shown for entries written with `_logger.LogMetrics(...)`. They are logged at `LogLevel.Information` with event `Metrics` (id 10000), so other providers such as `AddConsole()` see a normal information entry. For real application metrics, use `System.Diagnostics.Metrics`.
- `Method=>` is included when set via `using (_logger.BeginMethodScope())` (or a scope with key `MethodName`)
- `Client=>` is omitted when no customer context is active

---

## Options Reference

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `IncludeScopes` | `bool` | `true` | Include scope information in output |
| `ResolveMethodNameFromStackTrace` | `bool` | `false` | Fall back to the stack trace for the method name when no `MethodName` scope is active (slow) |
| `IncludeStackTrace` | `bool` | `true` | Include stack trace in exception output |
| `IncludeInnerExceptions` | `bool` | `true` | Include inner exception chain |
| `EnableSensitiveDataMasking` | `bool` | `true` | Apply sensitive data masking |
| `MaskPlaceholder` | `string` | `[REDACTED]` | Replacement text for masked values |
| `AdditionalSensitiveKeys` | `IList<string>` | `[]` | Extra keys to treat as sensitive |

---

## Sensitive Data Masking

The following field names and message patterns are masked by default:

`password`, `wachtwoord`, `token`, `access_token`, `refresh_token`, `secret`,
`client_secret`, `api_key`, `x-api-key`, `authorization`, `bearer`, `cookie`, `set-cookie`

Masking is applied to:
- Plain log message strings (pattern: `key=value`, `key: value`)
- Structured log property names
- Dictionary/state collections passed to the logger

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
- **Sink abstraction** — Currently writes to Console. A future `ITjiddeLogSink` abstraction can route output to files, databases, or external systems.
- **Structured output formats** — JSON or OTLP output can be added as alternative formatters.
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
│   ├── TjiddeLogger.cs                 # ILogger implementation
│   └── TjiddeLoggerProvider.cs         # ILoggerProvider implementation
├── Masking/
│   ├── ISensitiveDataMasker.cs           # Masking contract
│   └── SensitiveDataMasker.cs           # Default masking implementation
└── Options/
    └── TjiddeLoggerOptions.cs          # Configuration options
```

---

## License

Licensed under the [Apache License 2.0](LICENSE). Copyright © 2026 Tjidde Nieuwenhuizen.

If you redistribute this package or a modified version of it, keep the [NOTICE](NOTICE) file, as the license requires.
