---
type: architecture
title: "Architecture"
description: "The Resultful package family across its two repositories, what each package may depend on, and the target repository layout."
status: draft
tags: [resultful, architecture, packages]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Architecture

> The package family, the assertions repository and the layout are decided
> ([decisions 11](decisions/0011-package-family.md), [12](decisions/0012-assertions-live-in-resultful-assertions.md)
> and [13](decisions/0013-repository-layout.md)). Each satellite is confirmed when its milestone starts. Package
> names in *italics* don't exist yet.

## Two repositories

| Repository | Purpose | Packages |
|---|---|---|
| `Resultful/Resultful` (this one) | The result types and their integrations with .NET | `Resultful` and its satellites |
| `Resultful/Resultful.Assertions` (renamed from `FluentAssertions.Resultful`) | Assertions for result and union types, in every assertion library where that makes sense | `FluentAssertions.OneOf`, `FluentAssertions.Union`, `FluentAssertions.OneOf.Extended`, `FluentAssertions.Resultful`, and the *`Resultful.Assertions`* family |

The split follows the dependency direction. Assertion packages depend on the libraries they assert on, and on the
assertion libraries themselves. Nothing in this repository depends on them.

`FluentAssertions.OneOf` has the most downloads of any package in the family (about 388,000, against about 7,000
for `Resultful`). Most of its users don't use Resultful, so it keeps its package ID and its independence from
`Resultful`.

## This repository's packages

Each satellite is a separate package, so the core stays dependency-free
([decision 1](decisions/0001-core-is-dependency-free.md)). Each package has its own version, and a release
publishes only the packages that changed, as in `Resultful.Assertions`.

| Package | Purpose | May depend on | Targets |
|---|---|---|---|
| `Resultful` | `Result<T>`, `Result<T, TError>`, `Validation<T, TError>` and `Error` (*decision 14*), `Unit`, `VoidResult` (obsolete since 0.3, replaced by `Result<Unit>`), `Option<T>`, `Resultful.CodeAnalysis` attributes, composition, LINQ, async, operators | **nothing** | `netstandard2.0`, `net10.0`, `net11.0` |
| *`Resultful.Analyzers`* | Roslyn analyzers and code fixes, a separate package that a project opts into (the core carries no analyzer assets) | Roslyn (build time only), as a development dependency | `netstandard2.0` (analyzer) |
| *`Resultful.Generators`* | Incremental source generator for Tier 1 `[ClosedHierarchy]` plumbing/mappers and Tier 2 `[ResultShape]` union-backed structs ([decision 16](decisions/0016-source-generated-result-shapes.md)) | Roslyn (build time only), as a development dependency | `netstandard2.0` (analyzer) |
| `Resultful.OneOf` | `ToOneOf`; `ToResult`, `ToOption` back (`ToVoidResult` obsolete); `None` interop | `OneOf` ≥ 3.0.271 | as core |
| *`Resultful.AspNetCore`* | `ToHttpResult()` returning `ResultHttpResult<T, TError>`, its own `IResult` (not `Results<Ok<T>, ProblemHttpResult>`, so the error type stays visible to endpoint metadata), ProblemDetails (RFC 9457), error-to-status map; endpoint filter and Minimal API validation bridge to come (20b) | `Microsoft.AspNetCore.App` framework reference | `net10.0`, `net11.0` |
| *`Resultful.Text.Json`* | `System.Text.Json` converters, source-generation friendly, AOT-safe | in-box `System.Text.Json` | `net10.0`, `net11.0` |
| *`Resultful.FluentValidation`* | `validator.ValidateToResult(x)` | `FluentValidation` | as `FluentValidation` |
| *`Resultful.Diagnostics`* | Opt-in `ILogger` and `ActivitySource` hooks | `Microsoft.Extensions.Logging.Abstractions` | `net10.0`, `net11.0` |
| `Resultful.Assertions` | `ShouldSucceed`, `ShouldFail`, `ShouldFind<TLeaf>` on results, validations, options, generated shapes and their errors, with no assertion library (decision 18) | `Resultful` | as core |
| `Resultful.Assertions.AwesomeAssertions` | `result.Should().Succeed().Which…`, `.Fail().WithMessage(…)`, `.FailWith<TLeaf>()` | `Resultful.Assertions`, `AwesomeAssertions` ≥ 9.0.0 | as core |
| `Resultful.Assertions.FluentAssertions` | The same API, compiled from the AwesomeAssertions adapter's sources | `Resultful.Assertions`, `FluentAssertions` `[7.0.0, 8.0.0)` | as core |

Rules:

- **The core references nothing.** Anything that needs a package goes in a satellite. Features that need only
  in-box APIs on newer targets go in the core behind `#if`, for example generic-math operators and
  `System.Collections.Immutable` operators on `net10.0` and later.
- **Satellites target what their dependency targets.** A satellite drops `netstandard2.0` rather than add a
  polyfill package. Consumers on `netstandard2.0` keep the core.
- **Every package passes the package check** (`eng/verify-package.cs`): no `netstandard1.x` or `.NETFramework`
  groups, metadata, SourceLink and symbols.
- **Every package tracks its public API** with `PublicAPI.*.txt`, kept per target where the surface differs.
- **Every package is versioned on its own.** A satellite depends on the lowest core version it needs, and a release
  publishes only the packages whose sources, or whose project dependencies, changed.

## Packages in `Resultful.Assertions`

Proposed in [decision 12](decisions/0012-assertions-live-in-resultful-assertions.md):

| Package | Status | Depends on |
|---|---|---|
| `FluentAssertions.OneOf`, `FluentAssertions.Union`, `FluentAssertions.OneOf.Extended` | kept, IDs unchanged | `FluentAssertions`, `OneOf` |
| `FluentAssertions.Resultful` | kept until the replacement exists, then deprecated on NuGet in favour of `Resultful.Assertions.FluentAssertions` | `FluentAssertions`, `Resultful` |
| *`Resultful.Assertions`* | new: `ShouldSucceed()`, `ShouldFail()`, `ShouldFailWith(...)`, independent of any assertion library, throwing an exception every test framework reports as a failure | `Resultful` |
| *`Resultful.Assertions.FluentAssertions`* | new: `result.Should().Succeed().Which...` | `Resultful.Assertions`, `FluentAssertions` |
| *`Resultful.Assertions.AwesomeAssertions`* | new | `Resultful.Assertions`, `AwesomeAssertions` |
| *`Resultful.Assertions.Shouldly`*, *`Resultful.Assertions.TUnit`* | later, where each library's extension model fits | the library |

This repository's own tests don't use `Resultful.Assertions`. The assertions packages are built against a
published `Resultful`, so using them here would test the core against an older version of itself.

## Repository layout

The target layout, following the sibling projects (Iceberg.NET, Diffra):

```text
src/
  Resultful/                      core
  Resultful.Analyzers/            separate package
  Resultful.Generators/           separate incremental generator package
  Resultful.OneOf/  Resultful.AspNetCore/  Resultful.Text.Json/  ...
tests/
  Resultful.Tests/                NUnit examples (today's suite)
  Resultful.Properties.Tests/     xunit.v3 + CsCheck laws
  Resultful.NetStandard.Tests/    loads the netstandard2.0 asset
  Resultful.Analyzers.Tests/
  Resultful.Generators.Tests/
  Resultful.AotSmoke/             native-AOT publish check
samples/                          Minimal API, MediatR, Polly, EF Core
benchmarks/Resultful.Benchmarks/  BenchmarkDotNet
docs/  eng/
Resultful.slnx
```

The move (#33) touched:
- `Resultful.sln`, which becomes `Resultful.slnx`
- `.ls-lint.yml` globs
- the paths in `ci.yml`
- the `EndsWith("Tests")`/`EndsWith("Examples")` conditions and the `README.md`/`LICENSE` pack paths in
  `Directory.Build.props`, plus a `src/Directory.Build.props` for package settings
- the `PublicAPI` `AdditionalFiles`
- the path the package check is run with
