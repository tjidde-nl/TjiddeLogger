# Changelog

All notable changes to Tjidde.Logging are listed here, newest first.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `MetricsLoggerExtensions.MetricsEventId` (`Id = 10000`, `Name = "Metrics"`), plus the constants `MetricsEventName` and `MetricsEventIdValue`. Tjidde.Logging shows every entry with event name `Metrics` as `[METRICS]` in magenta.

### Changed
- `LogMetrics` now logs at `LogLevel.Information` with `MetricsEventId` instead of the custom level `(LogLevel)10`. Tjidde.Logging still shows these entries as `[METRICS]` and its own minimum level never drops them; other providers see a normal information entry. `LogMetrics(EventId, ...)` keeps the caller's event ID and only adds the name `Metrics` when it has none.

### Fixed
- `LogMetrics` threw an `ArgumentOutOfRangeException` when another provider, such as the Microsoft console logger (`AddConsole()`), was also registered.

### Deprecated
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
