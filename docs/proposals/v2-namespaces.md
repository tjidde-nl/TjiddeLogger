# Proposal: public namespace clean-up for Tjidde.Logging v2.0

| | |
|---|---|
| Status | Draft / for discussion |
| Applies to | `src/Tjidde.Logging` (current version 1.0.1) |
| Target | 2.0.0 (breaking), with preparatory non-breaking steps in 1.x |
| Code changes in this proposal | None — this document only describes the plan |

## 1. Motivation

The current public surface is spread over six namespaces that mirror the folder layout
(`Context`, `Extensions`, `Formatting`, `Logging`, `Masking`, `Options`). That causes three problems:

1. **`Tjidde.Logging.Logging` is redundant.** It contains a single public type (`TjiddeLoggerProvider`) and
   reads as a stutter in fully-qualified names and docs (`Tjidde.Logging.Logging.TjiddeLoggerProvider`).
2. **`Tjidde.Logging.Options` shadows `Microsoft.Extensions.Options.Options`.** Any code whose own
   namespace is `Tjidde.*` (our own tests and samples, and any consumer whose root namespace starts
   with `Tjidde`) resolves the simple name `Options` to *our namespace* before it considers types imported
   via `using Microsoft.Extensions.Options;`. `Options.Create(new TjiddeLoggerOptions())` then fails with
   CS0234 ("The type or namespace name 'Create' does not exist in the namespace 'Tjidde.Logging.Options'"),
   and the user has to write `Microsoft.Extensions.Options.Options.Create(...)` or add an alias. The
   .NET Framework Design Guidelines explicitly advise against namespace names that collide with
   well-known type names for this reason.
3. **Too many `using` directives for basic use.** A typical app that registers the logger, sets options in
   code, sets the customer context and adds a masked key needs four usings:

   ```csharp
   using Tjidde.Logging.Extensions;  // AddTjiddeLogger, UseTjiddeJsonFormat, BeginMethodScope, LogMetrics
   using Tjidde.Logging.Options;     // TjiddeLoggerOptions, TjiddeLogOutputFormat
   using Tjidde.Logging.Context;     // CustomerContext
   using Tjidde.Logging.Masking;     // MaskedKeysContext
   ```

   Compare with first-party providers: `builder.Logging.AddConsole()` needs no extra using at all, because
   `ConsoleLoggerExtensions` lives in `Microsoft.Extensions.Logging`.

## 2. Inventory of the current public surface (v1.0.1)

Internal types are listed for completeness because they can be moved freely (tests use
`InternalsVisibleTo("Tjidde.Logging.Tests")`).

| Namespace | Type | Kind | Visibility | Typical consumer |
|---|---|---|---|---|
| `Tjidde.Logging.Context` | `CustomerContext` | static class (`Current`, `Set`, `Clear`) | public | app code (middleware) |
| | `ICustomerContextAccessor` | interface | public | extensibility (DI replacement) |
| | `AsyncLocalCustomerContextAccessor` | sealed class | public | default implementation |
| `Tjidde.Logging.Extensions` | `TjiddeLoggingBuilderExtensions` | static class: `AddTjiddeLogger` (4 overloads), `UseTjiddeJsonFormat`, `UseTjiddeTextFormat` on `ILoggingBuilder` | public | `Program.cs` |
| | `MethodScopeLoggerExtensions` | static class: `BeginMethodScope(this ILogger, ...)` | public | app code |
| | `MetricsLoggerExtensions` | static class: `Metrics` (`LogLevel` field), `LogMetrics(this ILogger, ...)` (3 overloads) | public | app code |
| `Tjidde.Logging.Formatting` | `IExceptionFormatter` | interface | public | not pluggable today (see §3.3) |
| | `ExceptionFormatter` | sealed class | public | not pluggable today |
| `Tjidde.Logging.Logging` | `TjiddeLoggerProvider` | sealed class (`ILoggerProvider`, `ISupportExternalScope`, `[ProviderAlias("Tjidde")]`) | public | rarely referenced directly (DI, `AddFilter<T>`) |
| | `TjiddeLogger` | sealed class | internal | — |
| | `ConsoleLogProcessor` | sealed class | internal | — |
| `Tjidde.Logging.Masking` | `MaskedKeysContext` | static class (`Add`, `Remove`, `Clear`, `GetKeys`) | public | app code |
| | `IMaskedKeysAccessor` | interface | public | extensibility (DI replacement) |
| | `GlobalMaskedKeysAccessor` | sealed class | public | default implementation |
| | `ISensitiveDataMasker` | interface | public | not pluggable today |
| | `SensitiveDataMasker` | sealed class | public | not pluggable today |
| | `MessageTemplateRenderer` | static class | internal | — |
| `Tjidde.Logging.Options` | `TjiddeLoggerOptions` | sealed class | public | `Program.cs`, `appsettings.json` binding |
| | `TjiddeLogOutputFormat` | enum (`Text`, `Json`) | public | `Program.cs` |

Notes:

- The root namespace `Tjidde.Logging` currently contains **no types**.
- The configuration section name (`"TjiddeLogger"`) and the provider alias (`"Tjidde"`) are not namespace
  dependent and are **not** changed by this proposal; `appsettings.json` files keep working.

## 3. Proposed layout for 2.0

### 3.1 Principles

1. **Registration extensions on `ILoggingBuilder` live in `Microsoft.Extensions.Logging`.** This is the
   established convention (`AddConsole`, `AddDebug`, `AddEventLog`, `AddApplicationInsights`,
   `AddOpenTelemetry`, ...). `Program.cs` already has that namespace via implicit usings, so registration
   needs zero extra usings.
2. **Everything an application touches in normal use lives in the root namespace `Tjidde.Logging`:**
   options, the output-format enum, the provider, the two static "context" facades and the `ILogger`
   convenience extensions. One `using Tjidde.Logging;` (or a single `global using`) covers all day-to-day use.
3. **Extensibility points and implementation details live in descriptive sub-namespaces** that most
   users never import: `Tjidde.Logging.Context`, `Tjidde.Logging.Masking`, `Tjidde.Logging.Formatting`.
4. **No namespace may share its last segment with a well-known BCL / Microsoft.Extensions type or
   namespace** (`Options`, `Logging`, `Configuration`, `Extensions`, ...).
5. **`ILogger` extension methods are *not* put in `Microsoft.Extensions.Logging`.** Unlike the
   registration methods, `BeginMethodScope` and `LogMetrics` would then appear in IntelliSense on every
   `ILogger` in every project that references the package (including transitively), which is intrusive and
   increases the chance of name collisions with other libraries. They go into `Tjidde.Logging`, which apps
   import anyway.

### 3.2 Target mapping

| Type (v1) | v1 namespace | **v2 namespace** | Remarks |
|---|---|---|---|
| `TjiddeLoggingBuilderExtensions` | `Tjidde.Logging.Extensions` | **`Microsoft.Extensions.Logging`** | Same class name and signatures. |
| `TjiddeLoggerOptions` | `Tjidde.Logging.Options` | **`Tjidde.Logging`** | |
| `TjiddeLogOutputFormat` | `Tjidde.Logging.Options` | **`Tjidde.Logging`** | |
| `TjiddeLoggerProvider` | `Tjidde.Logging.Logging` | **`Tjidde.Logging`** | |
| `CustomerContext` | `Tjidde.Logging.Context` | **`Tjidde.Logging`** | Static facade used from app code/middleware. |
| `MaskedKeysContext` | `Tjidde.Logging.Masking` | **`Tjidde.Logging`** | Static facade used from app code. |
| `MethodScopeLoggerExtensions` | `Tjidde.Logging.Extensions` | **`Tjidde.Logging`** | |
| `MetricsLoggerExtensions` | `Tjidde.Logging.Extensions` | **`Tjidde.Logging`** | Optionally also expose the level as `TjiddeLogLevel.Metrics` (const) — out of scope here. |
| `ICustomerContextAccessor` | `Tjidde.Logging.Context` | `Tjidde.Logging.Context` (unchanged) | Extensibility point. |
| `AsyncLocalCustomerContextAccessor` | `Tjidde.Logging.Context` | `Tjidde.Logging.Context` (unchanged) | |
| `IMaskedKeysAccessor` | `Tjidde.Logging.Masking` | `Tjidde.Logging.Masking` (unchanged) | |
| `GlobalMaskedKeysAccessor` | `Tjidde.Logging.Masking` | `Tjidde.Logging.Masking` (unchanged) | |
| `ISensitiveDataMasker` | `Tjidde.Logging.Masking` | `Tjidde.Logging.Masking` (unchanged) | See §3.3. |
| `SensitiveDataMasker` | `Tjidde.Logging.Masking` | `Tjidde.Logging.Masking` (unchanged) | See §3.3. |
| `IExceptionFormatter` | `Tjidde.Logging.Formatting` | `Tjidde.Logging.Formatting` (unchanged) | See §3.3. |
| `ExceptionFormatter` | `Tjidde.Logging.Formatting` | `Tjidde.Logging.Formatting` (unchanged) | See §3.3. |
| `TjiddeLogger` (internal) | `Tjidde.Logging.Logging` | `Tjidde.Logging.Internal` | Internal, no public impact. |
| `ConsoleLogProcessor` (internal) | `Tjidde.Logging.Logging` | `Tjidde.Logging.Internal` | Internal, no public impact. |
| `MessageTemplateRenderer` (internal) | `Tjidde.Logging.Masking` | `Tjidde.Logging.Masking` or `.Internal` | Internal, no public impact. |

Resulting public namespaces in 2.0:

```
Microsoft.Extensions.Logging      TjiddeLoggingBuilderExtensions
Tjidde.Logging                    TjiddeLoggerOptions, TjiddeLogOutputFormat, TjiddeLoggerProvider,
                                  CustomerContext, MaskedKeysContext,
                                  MethodScopeLoggerExtensions, MetricsLoggerExtensions
Tjidde.Logging.Context            ICustomerContextAccessor, AsyncLocalCustomerContextAccessor
Tjidde.Logging.Masking            IMaskedKeysAccessor, GlobalMaskedKeysAccessor,
                                  ISensitiveDataMasker, SensitiveDataMasker
Tjidde.Logging.Formatting         IExceptionFormatter, ExceptionFormatter
```

`Tjidde.Logging.Extensions`, `Tjidde.Logging.Logging` and `Tjidde.Logging.Options` disappear.

Resulting `Program.cs` for the common case:

```csharp
using Tjidde.Logging;                      // only needed for options/enums/facades

builder.Logging.ClearProviders();
builder.Logging.AddTjiddeLogger(o => o.OutputFormat = TjiddeLogOutputFormat.Json);
```

and for the minimal case (`builder.Logging.AddTjiddeLogger();` or binding from `appsettings.json`),
**no Tjidde using at all**.

### 3.3 Side issue found during the inventory: non-pluggable public types

`ISensitiveDataMasker`/`SensitiveDataMasker` and `IExceptionFormatter`/`ExceptionFormatter` are public,
but `TjiddeLoggerProvider` constructs them itself (`new SensitiveDataMasker(...)`,
`new ExceptionFormatter(...)`) instead of resolving them from DI. Registering a custom implementation
therefore has no effect. Because 2.0 is a breaking release anyway, decide one of:

- **(a) make them pluggable** (resolve from DI with `TryAdd` defaults, as already done for
  `ICustomerContextAccessor`/`IMaskedKeysAccessor`), or
- **(b) make them internal** (and move to `Tjidde.Logging.Internal`), shrinking the public surface.

Recommendation: (a) for `IExceptionFormatter` (a realistic customisation), (b) or (a) for the masker
depending on whether masking is meant to be replaceable. This decision is independent of the namespace
move but should land in the same major version.

### 3.4 Alternatives considered

| Alternative | Why not chosen |
|---|---|
| Rename `Tjidde.Logging.Options` → `Tjidde.Logging.Configuration` and keep the rest | Solves the clash but still requires 3–4 usings; `Configuration` collides with `Microsoft.Extensions.Configuration` in the same way. |
| Put *everything* (including accessors, masker, formatter) in `Tjidde.Logging` | Simplest, but mixes rarely-used extensibility types into the namespace everyone imports. Acceptable fallback if the sub-namespaces feel like overkill for ~6 types. |
| Put the `ILogger` extensions (`BeginMethodScope`, `LogMetrics`) in `Microsoft.Extensions.Logging` too | Pollutes IntelliSense on every `ILogger` for every consumer; see principle 5. |
| Put registration extensions in `Microsoft.Extensions.DependencyInjection` | They extend `ILoggingBuilder`, not `IServiceCollection`; the logging-provider convention is `Microsoft.Extensions.Logging`. |

## 4. Migration path

### 4.1 Why the type moves cannot be done additively in 1.x

- **Moving a type to another namespace is both source- and binary-breaking.** The CLR identifies a type by
  namespace + name; `[TypeForwardedTo]` only forwards to *another assembly* with the *same* full name, so it
  cannot express a namespace rename inside one assembly.
- **We cannot keep a copy of a class in both namespaces.** Two distinct `TjiddeLoggerOptions` types would
  break `IOptions<TjiddeLoggerOptions>` (DI keys on the exact type) and confuse every user who imports both.
- **Duplicating the extension methods causes ambiguity (CS0121).** If 1.x shipped
  `Microsoft.Extensions.Logging.TjiddeLoggingBuilderExtensions` *next to* the existing
  `Tjidde.Logging.Extensions.TjiddeLoggingBuilderExtensions`, every existing user has both
  `using Microsoft.Extensions.Logging;` (implicit usings) and `using Tjidde.Logging.Extensions;` in scope.
  Both classes are found at the same lookup level, so `builder.Logging.AddTjiddeLogger(...)` becomes
  *"The call is ambiguous between ... and ..."* — a compile break in a minor version. Marking the old one
  `[Obsolete]` does not help: obsolete members still participate in overload resolution.
  The same applies to duplicating `BeginMethodScope`/`LogMetrics` into `Tjidde.Logging` for users who
  import both `Tjidde.Logging` and `Tjidde.Logging.Extensions`.
  (Additive duplicates *with different method names* would avoid the ambiguity, but would introduce a
  second, permanent name for the same API just to retire it again — not worth it.)

### 4.2 What can be done in 1.x (non-breaking)

| Step | Version | Change |
|---|---|---|
| 1 | 1.1 | Move the **internal** types (`TjiddeLogger`, `ConsoleLogProcessor`) to `Tjidde.Logging.Internal`. Public types stay put. This makes `Tjidde.Logging.Logging` contain only `TjiddeLoggerProvider` and shrinks the 2.0 diff. |
| 2 | 1.1 | Rename the samples' own namespaces away from `Tjidde.Logging.*` (e.g. `TjiddeSamples.DemoApp`) so they model what consumers see and stop being affected by the `Options` shadowing. |
| 3 | 1.1 | Docs (`wiki-en.md`, `wiki-nl.md`, `AI-INTEGRATION.md`, `README.md`): add an "Upcoming in 2.0" note with the mapping table, recommend a single `global using` file (see §5.3) so the eventual migration touches one file, and document the workaround for the `Options` clash: `using MsOptions = Microsoft.Extensions.Options.Options;`. |
| 4 | 1.1 | Decide §3.3 (pluggable vs. internal masker/formatter). If "pluggable" is chosen, resolving them from DI with `TryAdd` defaults is additive and can ship in 1.x already. |
| 5 | 1.x (last minor) | Optionally add `[Obsolete("Moves to namespace X in 2.0. See docs/proposals/v2-namespaces.md")]` to the types that will move. **Not recommended**: the warning cannot be fixed until 2.0 exists, and it breaks builds with `TreatWarningsAsErrors`. Prefer the release note + docs. |

### 4.3 What has to wait for 2.0 (breaking)

1. Move `TjiddeLoggingBuilderExtensions` to `Microsoft.Extensions.Logging`.
2. Move `TjiddeLoggerOptions`, `TjiddeLogOutputFormat`, `TjiddeLoggerProvider`, `CustomerContext`,
   `MaskedKeysContext`, `MethodScopeLoggerExtensions`, `MetricsLoggerExtensions` to `Tjidde.Logging`.
3. Delete the namespaces `Tjidde.Logging.Extensions`, `Tjidde.Logging.Logging`, `Tjidde.Logging.Options`.
4. Apply the §3.3 decision (if it makes types internal, that is breaking too).
5. Update tests, samples, wiki (EN + NL), `AI-INTEGRATION.md`, `README.md`, `CHANGELOG.md`
   (a "Breaking changes" section linking to the migration guide below).
6. Bump `<Version>` to `2.0.0`. Consider adding `Microsoft.CodeAnalysis.PublicApiAnalyzers`
   (`PublicAPI.Shipped.txt`) at the same time so future public-surface changes are deliberate.

A note on leftover `using` lines: after the upgrade, a remaining `using Tjidde.Logging.Options;` produces
CS0246 because the namespace no longer exists. That is intended — it points the user exactly at the line
to change — and the fix is mechanical (§5).

## 5. Migration guide for users (1.x → 2.0)

*This section is written so it can be copied into the 2.0 release notes / wiki.*

### 5.1 TL;DR

1. Remove `using Tjidde.Logging.Extensions;`, `using Tjidde.Logging.Options;` and `using Tjidde.Logging.Logging;`.
2. Add `using Tjidde.Logging;` where you use options, `TjiddeLogOutputFormat`, `CustomerContext`,
   `MaskedKeysContext`, `BeginMethodScope` or `LogMetrics`.
3. `AddTjiddeLogger(...)`, `UseTjiddeJsonFormat()` and `UseTjiddeTextFormat()` now work with only
   `using Microsoft.Extensions.Logging;` (included in the default implicit usings of ASP.NET Core and
   Worker/Console templates).
4. Keep `using Tjidde.Logging.Context;` / `using Tjidde.Logging.Masking;` / `using Tjidde.Logging.Formatting;`
   **only** where you implement or reference the accessor/masker/formatter interfaces or their default
   implementations.

No changes are needed in `appsettings.json` (section `TjiddeLogger`, provider alias `Tjidde`).
Method names, signatures and behaviour are unchanged.

### 5.2 Lookup table

| You used | Replace with |
|---|---|
| `using Tjidde.Logging.Extensions;` (for `AddTjiddeLogger`, `UseTjidde*Format`) | nothing (`Microsoft.Extensions.Logging`) |
| `using Tjidde.Logging.Extensions;` (for `BeginMethodScope`, `LogMetrics`, `MetricsLoggerExtensions.Metrics`) | `using Tjidde.Logging;` |
| `using Tjidde.Logging.Options;` | `using Tjidde.Logging;` |
| `using Tjidde.Logging.Logging;` | `using Tjidde.Logging;` |
| `using Tjidde.Logging.Context;` for `CustomerContext` | `using Tjidde.Logging;` |
| `using Tjidde.Logging.Context;` for `ICustomerContextAccessor` / `AsyncLocalCustomerContextAccessor` | unchanged |
| `using Tjidde.Logging.Masking;` for `MaskedKeysContext` | `using Tjidde.Logging;` |
| `using Tjidde.Logging.Masking;` for accessor/masker types | unchanged |
| `using Tjidde.Logging.Formatting;` | unchanged |
| Fully-qualified `Tjidde.Logging.Options.TjiddeLoggerOptions` | `Tjidde.Logging.TjiddeLoggerOptions` |
| Fully-qualified `Tjidde.Logging.Logging.TjiddeLoggerProvider` (e.g. in `AddFilter<T>`) | `Tjidde.Logging.TjiddeLoggerProvider` |
| Workaround `Microsoft.Extensions.Options.Options.Create(...)` / alias for the `Options` clash | can be reverted to `Options.Create(...)` |

### 5.3 Recommended: one global using

Create (or edit) `GlobalUsings.cs`:

```csharp
global using Tjidde.Logging;
```

or in the project file:

```xml
<ItemGroup>
  <Using Include="Tjidde.Logging" />
</ItemGroup>
```

### 5.4 Before / after

**1.x**

```csharp
using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;

builder.Logging.ClearProviders();
builder.Logging.AddTjiddeLogger(o => o.OutputFormat = TjiddeLogOutputFormat.Json);
CustomerContext.Set("customer-42");
MaskedKeysContext.Add("iban");
```

**2.0**

```csharp
using Tjidde.Logging;

builder.Logging.ClearProviders();
builder.Logging.AddTjiddeLogger(o => o.OutputFormat = TjiddeLogOutputFormat.Json);
CustomerContext.Set("customer-42");
MaskedKeysContext.Add("iban");
```

### 5.5 Binary compatibility

2.0 is **not binary compatible** with 1.x. Libraries compiled against 1.x that reference moved types
(for example a shared library that calls `AddTjiddeLogger` or references `TjiddeLoggerOptions`) must be
recompiled against 2.0; otherwise they fail at runtime with `TypeLoadException` / `MissingMethodException`.
If your solution has several projects referencing Tjidde.Logging, upgrade them together.

## 6. Open questions

1. Sub-namespaces for `Context`/`Masking`/`Formatting`, or everything in `Tjidde.Logging` (§3.4)?
2. Masker/formatter: pluggable or internal (§3.3)?
3. Ship a 1.x release that announces the change (docs + release note only), or go straight to 2.0?
4. Should `MetricsLoggerExtensions.Metrics` get a friendlier home (e.g. `TjiddeLogLevel.Metrics`) in the same
   major version?
