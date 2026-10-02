# Changelog

All notable changes to Tjidde.Logging are listed here, newest first.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `MetricsLoggerExtensions.MetricsEventId` (`Id = 10000`, `Name = "Metrics"`), plus the constants `MetricsEventName` and `MetricsEventIdValue`. Tjidde.Logging shows every entry with event name `Metrics` as `[METRICS]` in magenta.
- `TjiddeLoggerOptions.UseUtcTimestamp` (default `false`): write timestamps in UTC instead of local time. JSON `@timestamp` is ISO-8601 with the offset (`+00:00` in UTC).
- Timestamps come from `System.TimeProvider`: the one registered in DI, or `TimeProvider.System` when none is registered, so tests can use a `FakeTimeProvider`. New constructor overload `TjiddeLoggerProvider(IOptionsMonitor<TjiddeLoggerOptions>, ICustomerContextAccessor, IMaskedKeysAccessor, TimeProvider)`; the existing constructor still works and uses `TimeProvider.System`. On .NET 7 this adds a dependency on `Microsoft.Bcl.TimeProvider`.

- `MaskedKeysStore`: a non-static, thread-safe `IMaskedKeysAccessor` with `Add`, `Remove`, `Clear` and `GetKeys` that returns immutable snapshots, and the builder extension `UseIsolatedMaskedKeys()`, which registers one store per host as the `IMaskedKeysAccessor` (replacing an earlier registration, in any order relative to `AddTjiddeLogger`). Inject `MaskedKeysStore` to add keys that apply immediately to that host's loggers only, so hosts and tests in one process no longer share runtime keys. The static `MaskedKeysContext` stays the default and now uses a `MaskedKeysStore` internally. The README, wikis and AI integration guide now recommend the accessors and DI for testable code and describe the static classes as the convenience variant.

### Changed
- Options changes now apply to existing loggers immediately, without a restart: the provider listens to `IOptionsMonitor<TjiddeLoggerOptions>.OnChange`, and `AddTjiddeLogger(IConfiguration)` / `AddTjiddeLogger(IConfigurationSection)` rebind the options when the configuration reloads (for example `appsettings.json` with `reloadOnChange`). Options, masker, exception formatter and OpenTelemetry fallback `ActivitySource` are replaced together, so a log call never mixes old and new settings; the previous `ActivitySource` is disposed.
- `LogMetrics` now logs at `LogLevel.Information` with `MetricsEventId` instead of the custom level `(LogLevel)10`. Tjidde.Logging still shows these entries as `[METRICS]` and its own minimum level never drops them; other providers see a normal information entry. `LogMetrics(EventId, ...)` keeps the caller's event ID and only adds the name `Metrics` when it has none.
- JSON output: object cycles in logged values are written as `null` instead of failing, and object graphs deeper than 32 levels fall back to `ToString()`.
- All loggers of a provider share one `SensitiveDataMasker` (and its regular expressions) per configuration instead of building one per category; it is replaced when the options change.
- `AddTjiddeLogger` registers `TjiddeLoggerProvider` through a factory, so the container never has to choose between its constructors.
- `IsEnabled` caches the resolved minimum level per logger until the options change, instead of matching `CategoryMinimumLevels` on every call.

### Fixed
- `LogMetrics` threw an `ArgumentOutOfRangeException` when another provider, such as the Microsoft console logger (`AddConsole()`), was also registered.
- Logging no longer throws. A property value that cannot be serialized (unsupported type, throwing getter) falls back to its `ToString()`, or to `[unserializable: TypeName]` when that throws too; a value whose `ToString()` or enumeration throws no longer breaks the message (the template is used instead); a failing custom `IExceptionFormatter` falls back to exception type and message; scopes that throw are skipped; OpenTelemetry export failures are ignored. Any other failure while building the entry writes a minimal line with level, category, message and a note that rendering failed.
- A custom `IMaskedKeysAccessor` that returns the same mutable collection on every call: added or removed keys were not picked up after the first log call. The masker now compares the keys by content (only the immutable snapshots of `MaskedKeysContext` and `MaskedKeysStore` are compared by reference), builds from a copy, and no longer lets a concurrent rebuild overwrite a newer key set.

### Deprecated
- `TjiddeLoggerOptions.MinimumLevel` and `TjiddeLoggerOptions.CategoryMinimumLevels` (`TjiddeLogger:MinimumLevel`, `TjiddeLogger:CategoryMinimumLevels`) are marked `[Obsolete]` and will be removed in 2.0. Use the standard filter `Logging:Tjidde:LogLevel` in `appsettings.json` (or `AddFilter<TjiddeLoggerProvider>(...)`) instead. They still work and are still bound from configuration: the `Logging` filters run first, then these, so the stricter level wins.
- `MetricsLoggerExtensions.Metrics` (`(LogLevel)10`): not a valid `LogLevel`, and other providers throw on it. Use `LogMetrics(...)`, or `System.Diagnostics.Metrics` for real application metrics. Tjidde.Logging still recognizes the value.

## [1.0.1] - 2026-10-02

### Added
- Release notes: this changelog is shown on the NuGet "Release Notes" tab and ships inside the package as `CHANGELOG.md`.

## [1.0.0] - 2026-10-01

### Added
- First public release.
- `AddTjiddeLogger()` registration on top of Microsoft.Extensions.Logging, with consistent, structured output.
- Customer/tenant context on every log entry.
- Masking of sensitive data in log messages and properties.
- Readable exception formatting, including inner exceptions and stack traces.
- Support for .NET 7, 8, 9 and 10.
