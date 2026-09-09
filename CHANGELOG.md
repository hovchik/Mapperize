# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **More ways to declare mappers.** Mapping methods can now be:
  - `static` methods (call without constructing the mapper);
  - extension methods (`this` on the source parameter) for fluent `source.ToDto()` calls;
  - map-into-an-existing-instance methods — `void Update(TSource, TTarget)` or
    `TTarget Update(TSource, TTarget)` (populate a target in place, optionally returning it).
- Mapper types may be **nested inside another `partial` type** and may be a `static partial class`.
- **Dictionary mapping** for `Dictionary<K,V>`, `IDictionary<K,V>`, and `IReadOnlyDictionary<K,V>`
  (keys and values converted with the same engine).

### Fixed
- Nullable complex structs (e.g. `Point?` → `PointDto?`) now preserve `null` instead of mapping a
  default value.
- Nested-object mappings no longer emit a redundant null check that evaluated the source accessor
  twice; the generated helper already handles `null`, so the call site is now a single expression
  (a small correctness and throughput improvement for property getters with side effects).

### Changed
- `MPZ004` now reports any mapper whose type — or an enclosing type — is generic or not declared
  `partial`, replacing the previous top-level-only restriction.

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
