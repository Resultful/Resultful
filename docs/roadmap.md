---
type: roadmap
title: "Roadmap"
description: "The goal, Resultful 1.0, and the milestones that reach it: a safe beta, a dependency-free core, unions on .NET 11, and 1.0."
status: draft
tags: [resultful, roadmap]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Roadmap

## The goal: Resultful 1.0

Resultful is established when the core package has no dependencies, the API is stable across
`netstandard2.0`, `net10.0` and `net11.0`, the net11 union integration is chosen on evidence, and every release
is cut from CI with its package shape checked.

## Milestone 1: a beta that restores cleanly

The packaging regression that broke rig is fixed and cannot recur.

1. **Dependency fix.** OneOf floor 3.0.271; no `netstandard1.x` or `.NETFramework` groups; no
   `NETStandard.Library` or `System.ValueTuple` ([decision 5](decisions/0005-package-shape-is-checked.md)).
2. **Package metadata.** Readme, MIT licence expression, repository and commit (SourceLink), `snupkg`,
   deterministic CI builds.
3. **Package check.** A pack-and-inspect step fails on any of the above regressing.
4. **Sequential composition.** `Then`/`Sequence`/`Check` and their async forms
   ([decision 4](decisions/0004-sequential-composition-is-additive.md)).
5. **Hygiene.** AOT/trim flags on net8+, warnings as errors, `dotnet format` check, LF line endings.

**Done when** the fresh package resolves alone under Paket `strategy: min` on `net11.0`.

## Milestone 2: a dependency-free core

1. **Own storage.** `Result`, `VoidResult` and `Option` stop wrapping `OneOf<...>`; `Resultful.None` replaces
   `OneOf.Types.None` ([decision 1](decisions/0001-core-is-dependency-free.md)).
2. **`Resultful.OneOf`.** `ToOneOf()` (sync and `Task`), `FromOneOf` and `None` interop move into their own package.
3. **Targets.** `netstandard2.0`, `net10.0`, `net11.0`; SDK 11
   ([decision 2](decisions/0002-targets-netstandard20-net10-net11.md)).
4. **Characterisation tests first.** The current behaviour (defaults, nulls, conversions, accumulation, equality,
   async) is pinned before the storage changes.
5. **Migration notes** in the release notes.

**Done when** core packs with no dependency groups and all tests pass on all three targets.

## Milestone 3: unions on .NET 11

1. **Experiment.** Candidate integrations: `[Union]`/`IUnion` on the existing structs, union-backed storage, and
   interop with user-written unions ([decision 3](decisions/0003-unions-are-chosen-by-measurement.md)).
2. **Correctness matrix first.** Overlapping case types (`Result<List<string>>`, `Result<object>`,
   `Result<string, string>`), null successes, consumption from another assembly under warnings as errors.
3. **Benchmarks.** BenchmarkDotNet throughput and allocations, net10 against net11.
4. **Pick.** A decision document with the numbers; the maintainer can veto it.

**Done when** the chosen integration ships behind `#if NET11_0_OR_GREATER` with its tests.

## Milestone 4: tests and CI at scale

1. **Test mix.** Existing NUnit tests stay; new xunit.v3 (Microsoft.Testing.Platform) suites add CsCheck
   property tests for the laws ([decision 6](decisions/0006-tests-are-a-mix.md)).
2. **Coverage ratchet** in CI.
3. **Target matrix.** Every test runs on net10.0 and net11.0, and one run loads the `netstandard2.0` asset.
4. **CI hardening** in rig's style ([decision 7](decisions/0007-ci-follows-rig.md)).

## Milestone 5: 1.0

API review of every public member, stable decisions, documentation for every operator, and a non-prerelease
version.
