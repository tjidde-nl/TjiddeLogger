# Changelog

All notable changes to Tjidde.Logging are listed here, newest first.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

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
