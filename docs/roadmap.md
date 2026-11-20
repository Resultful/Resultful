---
type: roadmap
title: "Roadmap"
description: "The long-term path for Resultful as milestones of small pull requests: foundations, the language surface, a dependency-free core, tooling, framework packages, unions, 1.0 and beyond, plus the assertions track."
status: draft
tags: [resultful, roadmap]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Roadmap

## The goal

Resultful should make failure part of a C# signature without making the code around it any less C#
([vision](vision.md)). Long term, that means:
- a dependency-free core that feels native to the language
- satellite packages that remove the friction at each framework boundary
- an assertions family for every assertion library people use

The packages and their dependency rules are in [the architecture](architecture.md).

Each milestone is a series of small pull requests, each mergeable on its own and each leaving `main` releasable.
Merging never publishes: a release is a manual workflow run on `main`. There are no calendar dates. A milestone
is done when its "Done when" holds.

## Done

| Milestone | Pull requests |
|---|---|
| **Packaging fix and sequential composition** (`0.2.0-beta06`): OneOf floor 3.0.271, clean dependency groups, metadata, SourceLink, `Then`/`Sequence`/`Check` ([decisions 4](decisions/0004-sequential-composition-is-additive.md), [5](decisions/0005-package-shape-is-checked.md)) | #21 |
| **CI to current standards**: least privilege, format check, pack and package check, manual publishing, zizmor, ls-lint, Dependabot ([decision 7](decisions/0007-ci-follows-rig.md)) | #22 |
| **Current .NET standards, part one**: SDK 11, `net11.0`, public API tracking ([decision 2](decisions/0002-targets-netstandard20-net10-net11.md)) | #24 |
| **Direction**: [Seamless with .NET](explorations/seamless-with-dotnet.md), [decision 10](decisions/0010-default-is-not-success.md) | #28, #29 |
| **Long-term plan**: this roadmap, [the architecture](architecture.md), decisions 9, 11, 12 and 13 accepted | #30, #31, #32 |

## M1. Foundations (`0.2.x`, no break)

Structure and safety nets before anything changes behaviour.

- Repository layout: `src/`, `tests/`, `samples/`, `benchmarks/`, `.slnx` ([decision 13](decisions/0013-repository-layout.md)).
  The first pull request, with no code changes.
- Per-package releases ([decision 11](decisions/0011-package-family.md)): a version per project, change detection
  by path and project graph, a tag per package, and the package check run on every package. Follows the design
  in `Resultful.Assertions`.
- XML documentation required on public members; `AnalysisLevel latest-all` with explicit opt-outs; nullable
  audit; per-target `PublicAPI` files.
- Testing at scale ([decision 6](decisions/0006-tests-are-a-mix.md)):
  - Characterisation tests that pin today's behaviour, including the two hazards: `default` is a success, and a
    bare string converts to an error.
  - xunit.v3 on Microsoft.Testing.Platform, with CsCheck laws.
  - A coverage ratchet in CI.
  - A run that loads the `netstandard2.0` asset.

**Done when** the new layout builds in CI, a release run publishes only the packages that changed, the
characterisation suite pins every public operator's current behaviour, and coverage can't fall.

## M2. The language surface (`0.2.x`, additive)

[Seamless with .NET](explorations/seamless-with-dotnet.md), sections 1, 4 and 5.

- Named methods first, on every target:
  - `IsSuccess`/`IsFailure` with `[MemberNotNullWhen]`
  - `TryGetValue`/`TryGetError`
  - `Deconstruct`
  - `OrElse`, `ValueOr`
- Operators over those methods, opt-in through `using Resultful.Operators;`
  ([decision 9](decisions/0009-operators-are-opt-in-sugar.md)):
  - `+ - *` with error accumulation
  - `|` and `||` fallback
  - `>>` bind
  - `&` tuple combine
  - the same operators on `Task<Result<T>>`
  - a precedence table in the README
- Collection operators on results of arrays, immutable lists, sets and dictionaries, with equality from the
  collection's comparer (still under exploration).
- Async: `ValueTask` on new overloads, `IAsyncEnumerable<Result<T>>` helpers, `CancellationToken` everywhere it
  applies.

**Done when** every operator has a named equivalent, tests for its laws, and a row in the README's operator
table.

## M3. A dependency-free core (`0.3.0-beta01`, breaking)

[Decisions 1](decisions/0001-core-is-dependency-free.md), [8](decisions/0008-version-0-3-0.md) and
[10](decisions/0010-default-is-not-success.md).

- Own storage in every type, with a state byte. Zero means uninitialised and throws on use.
- An `[Obsolete(error: true)]` parameterless constructor on each result type.
- `Resultful.None`; a new `Resultful.OneOf` package for interop.
- The package check covers both packages and allows a core with no dependency groups.
- Release notes with the migration, and the characterisation tests from M1 showing exactly what changed.

**Done when** the `Resultful` package has no dependencies, resolves alone under Paket `strategy: min`, and
`default(Result<T>)` can't pass as a success.

## M4. Tooling

- `Resultful.Analyzers`, a separate package ([decision 11](decisions/0011-package-family.md)). Rules:
  - a result that is never observed
  - `Value` used without a check
  - `default(Result<T>)` or a `default` literal where a result is expected
  - `Result<string>` built from a bare string
  - a `Match` that should be a `switch`
- Code fixes where the rewrite is unambiguous.

**Done when** each rule has positive and negative tests, and the samples build with no analyzer warnings.

## M5. Framework packages

[The architecture](architecture.md) lists each package's dependencies.

- A structured `Error` type in the core: `Code`, `Description`, `Type`, `Metadata`. **This is additive**:
  `Result<T>` keeps message errors, and `Error` is used through `Result<T, Error>`.
- `Resultful.AspNetCore`: typed results, ProblemDetails (RFC 9457), endpoint filter, error-to-status map,
  Minimal API validation bridge.
- `Resultful.Text.Json` and an AOT smoke test: `IsAotCompatible` on the core, and a native-AOT sample published
  in CI.
- `Resultful.FluentValidation`, `Resultful.Diagnostics`.
- Samples (not packages): Minimal API, MediatR pipeline behaviour, Polly, EF Core.

**Done when** a Minimal API sample returns `Result<T>`, OpenAPI shows both outcomes, and the AOT sample
publishes with no trim or AOT warnings.

## M6. Unions on .NET 11

[Decision 3](decisions/0003-unions-are-chosen-by-measurement.md).

- A custom `[Union]` with a member provider and non-boxing `TryGetValue`.
- A correctness matrix covering overlapping case types and the zero value, then BenchmarkDotNet results for
  net10 against net11.
- Shipped behind `#if NET11_0_OR_GREATER`, with a decision recording the numbers.

**Done when** `result switch { … }` is exhaustive on `net11.0` with no boxing on the hot path, or the decision
records why not.

## M7. Stable (`1.0.0`)

- API review of every public member, every decision `stable`, documentation for every operator and package.
- The core and every satellite that is ready move to `1.0.0`. Packages are versioned on their own, so a satellite
  that isn't ready stays in preview.

## Beyond 1.0

- Each new .NET release (`net12.0` is expected in November 2027) is added as a target, and the oldest
  out-of-support one is dropped from the tests (not from `netstandard2.0`).
- Mutation testing (Stryker) on the core, as in the sibling projects.
- A documentation site built from `docs/` and the XML docs.
- Community files: `CONTRIBUTING`, `SECURITY`, a `CHANGELOG` that replaces `RELEASE_NOTES`.

## The assertions track (`Resultful/Resultful.Assertions`)

[Decision 12](decisions/0012-assertions-live-in-resultful-assertions.md). This runs in parallel, in the other
repository.

- **A1 (urgent).** `FluentAssertions.Resultful` depends on `Resultful 0.2.0-beta05`, which carries the NU1903
  chain. It moves to `0.2.0-beta06` once that is published.
- **A2.** Rename the repository to `Resultful.Assertions` (GitHub redirects the old URLs). Add a vision and an
  OKF docs bundle, and bring CI to this repository's standard.
- **A3.** Add `Resultful.Assertions`, independent of any assertion library, plus the `FluentAssertions` and
  `AwesomeAssertions` adapters.
- **A4.** Deprecate `FluentAssertions.Resultful` on NuGet in favour of `Resultful.Assertions.FluentAssertions`.
  `FluentAssertions.OneOf` and `FluentAssertions.Union` keep their IDs.
- **A5.** Add `Shouldly` and `TUnit` adapters where their extension models fit, and track M3's breaking release.
