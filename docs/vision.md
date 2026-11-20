---
type: vision
title: "Resultful: Vision"
description: "Why Resultful exists, what it is and is not, who it is for, its principles and what success looks like."
status: draft
tags: [resultful, vision]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Resultful: Vision

> Draft. The specifics are in [the roadmap](roadmap.md) and [the decisions](decisions/index.md).

## The problem

C# code that can fail usually says so with exceptions, `null`, `bool TryX(out ...)` or ad-hoc status objects.
None of them appear in the type, so callers forget to handle them, failures from independent checks cannot be
collected, and a sequence of steps that can each fail turns into nested `if`s or `try` blocks. Railway Oriented
Programming fixes this in F#. C# had no idiomatic equivalent, so Resultful was built on top of
[OneOf](https://github.com/mcintyre321/OneOf) to provide one.

Two things have changed since then:

- **The dependency became the liability.** OneOf's old `netstandard1.3` group dragged `NETStandard.Library 1.6.1`
  and the vulnerable `System.Net.Http` / `System.Text.RegularExpressions 4.3.0` into net8+ apps that resolve
  lowest-matching versions (Paket, `strategy: min`). `0.2.0-beta05` broke `paket install` in rig for that reason.
  A small error-handling library should not have a dependency graph a consumer has to audit.
- **The language is catching up.** C# 15 unions (`union R(T, E);`, `[Union]`/`IUnion`, .NET 11) give the compiler
  exhaustive `switch` and case conversions natively. Resultful should fit with them, not compete with them.

## What Resultful is

- **Five small result types.** `Result<T, TError>`, `Result<T>` (string errors), `VoidResult<TError>`,
  `VoidResult` and `Option<T>`: readonly structs whose success or failure is part of the signature.
- **A railway, in C#'s own idiom.** `Map`, `Bind`, `Tee`, `Match`, implicit conversions from either case, LINQ
  query syntax, and a `Task` overload of every operator, so async chains read like sync ones.
- **Accumulation where it makes sense, short-circuiting where it does not.** `Plus`/`Fold`/`Reduce` collect every
  error from independent checks; `Then`/`Sequence`/`Check` run dependent steps in order and stop at the first
  failure.
- **Dependency-free.** The core `Resultful` package depends on nothing on any target. Interop with other
  libraries lives in its own package (`Resultful.OneOf` first), so nobody inherits a graph they did not ask for.
- **Union-aware on .NET 11.** On `net11.0` the types take part in C# unions where that proves to be correct and
  fast, behind `#if NET11_0_OR_GREATER`; `netstandard2.0` and `net10.0` keep the same API without them.
- **Cheap.** No allocation on the success path for value types, AOT- and trim-compatible, no reflection.

## What Resultful is not

- **Not a general functional-programming library.** No `Either` zoo, monad transformers, or immutable collections;
  [LanguageExt](https://github.com/louthy/language-ext) does that.
- **Not a union library.** It used OneOf for storage. It will use C# unions where they fit. It does not offer its own
  general-purpose union types.
- **Not an exception replacement for bugs.** Programming errors still throw (null arguments, broken invariants);
  results are for failures a caller is expected to handle.
- **Not a validation framework.** It gives you the plumbing to collect errors, not rules, messages or localisation.

## Who it is for

C# developers who want failure in their signatures without leaving C#'s idioms: application and service code
(controllers, handlers, pipelines) and libraries that must not take a heavy dependency. It supports
`netstandard2.0` for libraries that still target it, and `net10.0`/`net11.0` for current applications, including
those resolved with Paket and audited with NuGet's vulnerability checks.

## Principles

- **Zero dependencies in the core.** Interop goes in separate packages. A dependency graph is part of the API.
- **Checked, not remembered.** Package shape (dependency groups, floors, metadata, symbols), formatting, analyzers
  and coverage are enforced in CI; a regression fails the build, not a consumer's restore.
- **Additive by default, breaking on purpose.** New operators extend the existing API. A break is a decision that is
  recorded in a decision document and in the release notes with a migration path.
- **Measured, not assumed.** Performance choices (storage layout, union integration) carry benchmark and
  allocation numbers, and correctness is checked before speed is measured.
- **Same behaviour on every target.** `netstandard2.0`, `net10.0` and `net11.0` pass the same tests, and the
  `netstandard2.0` asset is tested as such, not only through a newer target.
- **Laws, not just examples.** The types obey the monad and accumulation laws, and property tests check them.

## Success

- `paket install` and `dotnet restore` with auditing enabled resolve Resultful with no transitive packages
  besides its own, on every supported target.
- Every public operator is covered by example tests and the laws by property tests, with coverage held by a
  ratchet in CI.
- On `net11.0`, a Resultful type can be matched exhaustively with `switch` and interoperates with user-written
  unions, with no regression in allocation or throughput against `net10.0`.
- A release is cut from CI alone: build, test on every target, pack, inspect the package, publish.
