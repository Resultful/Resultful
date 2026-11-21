---
type: decision
title: "Assertions for Resultful live in this repository"
description: "Resultful.Assertions and its adapters are built here against the in-repo core and released from this repository (decision 22); the OneOf and union assertion packages stay in the other repository. Partly supersedes decision 12."
status: stable
tags: [resultful, decision, assertions]
decided:
  by: human:rtkelly13
  at: 2026-11-21
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# 18. Assertions for Resultful live in this repository

This partly supersedes [decision 12](0012-assertions-live-in-resultful-assertions.md). The packages that assert
on Resultful's types move here. The packages that assert on OneOf and other unions stay in
`Resultful/Resultful.Assertions`.

| Package | Where | Depends on |
|---|---|---|
| `Resultful.Assertions` | here, new | `Resultful` only |
| `Resultful.Assertions.AwesomeAssertions` | here, new | `Resultful.Assertions`, `AwesomeAssertions` |
| `Resultful.Assertions.FluentAssertions` | here, new | `Resultful.Assertions`, `FluentAssertions` 7.x |
| `FluentAssertions.OneOf`, `FluentAssertions.Union`, `FluentAssertions.OneOf.Extended` | `Resultful.Assertions` repository, unchanged | `FluentAssertions`, `OneOf` |
| `FluentAssertions.Resultful` | deprecated on NuGet, pointing at `Resultful.Assertions.FluentAssertions` | — |

- `Resultful.Assertions` works with no assertion library. `ShouldSucceed()` returns the value, `ShouldFail()`
  returns the error, and `ShouldFind<TLeaf>()` on that error returns the leaf, searching as `Error.TryFind` does:
  `result.ShouldFail().ShouldFind<NotFound>()`. (A single `ShouldFailWith<TLeaf>()` on the result can't compile:
  C# can't infer the result's own type arguments once one is written.) A failure throws an
  exception that every test framework reports. The failure message uses `Error.ToString()`, which never prints a
  leaf's own properties.
- The adapters add each library's native style: `result.Should().Succeed().Which…`,
  `.Fail().WithError<NotFound>().WithContext(…)`. AwesomeAssertions is the primary adapter. The FluentAssertions
  adapter supports 7.x only, because 8 and later is commercially licensed.
- They work on generated shapes, closed hierarchies and, on `net11.0`, error unions.
- They share the repository's single version, like every other package here.
- The Shop and the end-to-end tests use them. The core's own law tests keep using `Match` directly, so the core
  isn't checked by code that depends on the core.
- Shouldly and TUnit adapters follow where their extension models fit.

Why:
- Decision 12 kept them apart because the assertions were built against a published `Resultful`, so this
  repository's tests couldn't use them. Built here, they reference the core directly and move with it.
- Useful assertions need 0.3's types (`Error`, `TryFind`, shapes, hierarchies). Released separately, they would
  always trail the core.
- `FluentAssertions.Resultful` contains no assertions today, so nothing is migrated.
- The OneOf and union packages don't depend on `Resultful`, and most of their users don't use it. Moving them
  would disturb them for no gain.

Consequences:
- `Resultful.Assertions` names a package in this repository and a repository that no longer ships it. Renaming
  the other repository is the maintainer's choice; GitHub redirects old names.
- That repository's roadmap drops R2 and R3 (the Resultful core and adapters) and points here.
- `docs/architecture.md` and the roadmap's assertions track follow when the packages land.
