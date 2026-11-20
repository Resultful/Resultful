---
type: log
title: "Log"
description: "What changed in the Resultful documents, by date."
status: stable
tags: [resultful, log]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Log

## 2026-11-20

- *Building the generator* added, folded into the error design branch: diagnostics `RFG0001`–`RFG0006` for deep
  and awkward hierarchies, cases ordered most-derived first, a two-stage incremental pipeline after
  `Parquet.SourceGenerator`, a size budget for generated members and IL, and checks on the emitted `.g.cs` with no
  golden files. Verified: CS0051, CS0552 and CS8510 behind three of the rules, and that an extension on an
  interface the shape implements binds (boxing) while a constrained generic doesn't (CS0411). Decision 16 gains
  item 10.

- Review of the error design, folded into one branch (decisions 14, 15 and 16):
  - Decision 16 amended by the maintainer: narrow, operation-named shapes (`PlaceOrderResult`) replace the generic
    `OrderResult<T>`, which misclassified an overlapping `T` at run time (verified); an overlapping success type
    is a generator error; conversions into a shape (verified); valueless shapes succeed with `Unit`; `ManyErrors`
    makes a shape that gathers. Proposed: a shape wraps one core value, and the generator writes only what
    depends on the user's types (verified with stand-in core types).
  - Decision 14: `WithContext` adds a note and keeps the error's type (verified); the implicit `string`
    conversion is cut; `ValidationFailed`, `Combine` and `AsResult` added; `Result<Unit>` replaces the void
    types; `Validation` moves to M2. One Still open list.
  - Decision 15: a `null` arm for nullable inputs; the cross-assembly rule is the guard, since a record's copy
    constructor must stay `protected` and another assembly derives through it (verified); the generator as a second reader; `[ContextWrapper]` dropped.
  - *The error shape* gains shape 8 (a generated result shape); *Errors in practice* uses it.

- Decision 16 accepted: source-generated `[ClosedHierarchy]` plumbing and `[ResultShape]` union-backed structs
  (`Resultful.Generators`) become the centrepiece consumer experience — flat, exhaustive `.NET 11` `[Union]`
  switching over success and every closed error leaf. Roadmap (goal, M4, M6), architecture, and vision updated.

- Exploration *Source-generated error hierarchies and result shapes* added: Tier 1 (`[ClosedHierarchy]` closure,
  exhaustive `Match`/`Switch`, outer-to-inner `Causes()` mapper) and Tier 2 (`[ResultShape]` domain result structs
  with widening to `Result<T>` and `.NET 11 [Union]` flat/grouped exhaustive `switch` over closed error leaves),
  verified with a `netstandard2.0` incremental generator probe on SDK 11.0.100, including empirical verification of
  `System.Text.Json` generator-to-generator blindness.

- Decision 15 proposed: analyzer rules read attributes (`[MustObserve]`, `[ClosedHierarchy]`) matched by name, so
  users' own types get the same checks. A probe confirmed a suppressor can remove CS8509 when every type of a
  closed hierarchy is matched, and keeps it otherwise. Exploration *Analyzers for your own types* added.

- *Errors in practice* added: ten use cases from a consuming application (endpoint statuses, a sign-up form,
  validate then act, database context, a payment adapter, a queue handler, ignored results, tests, a library's
  own result type, migrating from strings), each with what it catches.

- *The error shape* added: how decisions 14 and 15 fit together, how it differs from other .NET result libraries,
  and seven shapes an application builds (open, closed, nested closed, layered context, validation, boundary
  mapping, your own result type). Nested closed hierarchies verified with the suppressor probe.

- Decision 14 proposed: `Result` holds one error, `Validation` a non-empty list, and an abstract `Error` record
  is the default error for both. Exploration *One error or many* and reference *Error handling: prior art*
  added (ErrorOr, FluentResults, LanguageExt, CSharpFunctionalExtensions, Ardalis.Result, dotNext, OneOf, Go,
  Rust, ZIO, Effect, F#, Swift, Kotlin, Haskell, Zig). Roadmap M5's structured error and the seamless
  exploration's section 6 item are marked as under review.

- Layout moved (#33): `src/Resultful`, `tests/Resultful.Tests`, `samples/Resultful.Examples`, `Resultful.slnx`.
  Package settings are in `src/Directory.Build.props`; test and sample settings in their folders' props files,
  replacing the project-name conditions. No code changed.

- Decisions 9, 11 and 13 accepted. Operators are opt-in through `Resultful.Operators`. Packages are versioned on
  their own and only what changed is published, as in `Resultful.Assertions`. `Resultful.Analyzers` is a separate
  package. The satellite set is accepted as recommended and confirmed per milestone. The layout move is the first
  M1 pull request. Decision 10's front matter repaired.

- Decision 12 accepted: `Resultful.Assertions` holds many assertion projects and publishes only what changed.
  The rename, its vision and the selective publishing were handed to a session in that repository.
- Long-term plan: roadmap rewritten as milestones (M1 to M7, beyond 1.0, and an assertions track); architecture
  added; decisions 11 to 13 proposed (package family, `Resultful.Assertions`, repository layout). The
  exploration gains the collections probe.
- Decision 10 accepted: an invalid zero value, an obsolete-error parameterless constructor, and an analyzer rule.
- Exploration *Seamless with .NET*: C# 14 extension operators probed against the current types; two
  existing hazards found (`default` is success; a bare string converts to an error). Decisions 9 and 10 proposed.
- Roadmap step 3 done (#24): SDK 11, `net11.0`, public API tracking.
- Documents started as an OKF bundle, following the rtkelly-labs projects: vision, roadmap, decisions, log.
- Vision rewritten around the project's intention rather than its history.
- Roadmap restructured as a series of pull requests to a stable release; decision 8 updated to match.
- Decision 7 updated: ls-lint and Dependabot added, docs-only gate left out, publishing is a manual workflow run.
- Decisions 1 to 8: a dependency-free core with `Resultful.OneOf`; three targets with net11 union behaviour
  behind `#if`; unions chosen by experiment; additive sequential composition; package shape checked in CI;
  a mixed test stack; rig-style CI; `0.3.0-beta01`.
