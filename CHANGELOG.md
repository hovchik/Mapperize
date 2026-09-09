# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-09-09

Initial release.

### Added
- Roslyn incremental source generator that implements `partial` mapping methods on
  `[Mapper]`-annotated classes.
- Flat property mapping (case-insensitive by default).
- `[MapProperty]` renames and `[MapperIgnoreTarget]` ignores.
- Automatic, de-duplicated nested-object mapping.
- Collection mapping for arrays, `List<T>`, `HashSet<T>`, and interface/read-only variants.
- Enum mapping by name (default) or by value.
- Nullable-value handling and implicit/explicit numeric conversions.
- Constructor- and positional-record-based construction.
- Compile-time diagnostics `MPZ001`–`MPZ004`.
- Zero runtime dependencies; Native AOT / trimming friendly; `netstandard2.0` support.
- xUnit test suite, runnable sample, and a BenchmarkDotNet suite (vs AutoMapper and hand-written).
