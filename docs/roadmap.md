---
type: roadmap
title: "Roadmap"
description: "The path to a stable Resultful as a series of pull requests: packaging, CI, current .NET standards, testing, a dependency-free core, unions, and 1.0."
status: draft
tags: [resultful, roadmap]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Roadmap

## The goal: a stable Resultful

Resultful is stable when the core package has no dependencies, the API is reviewed and fixed across
`netstandard2.0`, `net10.0` and `net11.0`, the behaviour is pinned by example and property tests on every target,
and every release is cut from CI with its package shape checked.

It gets there as a series of small pull requests, each mergeable on its own and each leaving `main` releasable.
Merging never publishes: a release is a manual workflow run on `main`.

## 1. Packaging fix and sequential composition (`0.2.0-beta06`)

- OneOf floor 3.0.271, with no `netstandard1.x` or `.NETFramework` groups and no `NETStandard.Library` or
  `System.ValueTuple` ([decision 5](decisions/0005-package-shape-is-checked.md)).
- Package metadata: readme, licence expression, repository and commit (SourceLink), `snupkg`, deterministic builds.
- A pack-and-inspect check.
- `Then`, `Sequence` and `Check` ([decision 4](decisions/0004-sequential-composition-is-additive.md)).
- LF line endings and a `dotnet format` baseline. This documentation bundle. Release notes.

**Done when** the package resolves alone under Paket `strategy: min` on `net11.0`.

## 2. CI to current standards

Following [decision 7](decisions/0007-ci-follows-rig.md):

- Least-privilege tokens, `persist-credentials: false`, SDK from `global.json`, NuGet cache.
- A format check, pack and the package check on every pull request.
- Publishing becomes a manual workflow run on `main`; merging never publishes.
- zizmor, ls-lint (naming, no shell scripts), Dependabot, and the package check as a C# file-based app.
- The required check stays `build`.

## 3. Current .NET standards

[Decision 2](decisions/0002-targets-netstandard20-net10-net11.md).

- SDK 11.0.100, with `net11.0` added to the targets.
- Public API tracking (`PublicAPI.*.txt`) so every surface change shows in review.
- XML documentation generated and required on public members.
- `AnalysisLevel latest-all` with explicit opt-outs.
- Nullable annotations audited.

## 4. Testing at scale

Following [decision 6](decisions/0006-tests-are-a-mix.md):

- Characterisation tests that pin today's behaviour: defaults, nulls, conversions, accumulation, equality, async.
- An xunit.v3 project on Microsoft.Testing.Platform with CsCheck property tests for the laws.
- A coverage ratchet in CI.
- Tests run on `net10.0` and `net11.0`, and one run loads the `netstandard2.0` asset.

This goes before the storage change on purpose: the next pull request is checked against these tests.

## 5. A dependency-free core (`0.3.0-beta01`)

[Decision 1](decisions/0001-core-is-dependency-free.md) and [decision 8](decisions/0008-version-0-3-0.md).

- Own storage in `Result`, `VoidResult` and `Option`, with `Resultful.None`.
- A new `Resultful.OneOf` package for `ToOneOf()`, `FromOneOf` and `None` interop.
- The package check covers both packages.
- Release notes with the migration.

## 6. Unions on .NET 11

[Decision 3](decisions/0003-unions-are-chosen-by-measurement.md).

- Correctness matrix, then BenchmarkDotNet throughput and allocations, net10 against net11.
- A decision with the numbers.
- The chosen integration ships behind `#if NET11_0_OR_GREATER`.

## 7. Stable (`1.0.0`)

- API review of every public member.
- Every decision marked `stable`.
- Documentation for every operator.
- A non-prerelease version.
