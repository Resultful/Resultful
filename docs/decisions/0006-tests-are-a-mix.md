---
type: decision
title: "Tests are a mix: NUnit examples, xunit.v3 property tests"
description: "Keep the NUnit suite; add xunit.v3 on Microsoft.Testing.Platform with CsCheck and a coverage ratchet."
status: proposed
tags: [resultful, decision]
decided:
  by: ""
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 6. Tests are a mix: NUnit examples, xunit.v3 property tests

The maintainer asked for a large expansion of the tests, and "maybe a mix" of the existing stack and a modern one.

Proposed: the existing NUnit + FluentAssertions example tests stay. New suites use xunit.v3 on
Microsoft.Testing.Platform, CsCheck property tests (monad laws for `Map`/`Bind`, short-circuiting for
`Then`/`Sequence`, error accumulation for `Plus`/`Fold`/`Reduce`, sync/async equivalence), and coverlet
coverage with a ratchet (floor raised as coverage improves, never lowered). Tests run on `net10.0` and
`net11.0`, and one run loads the `netstandard2.0` asset.

How it runs: `eng/run-tests.cs` finds every `tests/*/*.csproj` and runs each target framework it declares
(`net481` only on Windows), plus one run per culture a project lists in `TestCultures`: the 0.2 suite also runs
under `tr-TR` and the invariant culture. It writes a TRX file and a count per run. CI fails if a run's total or
skipped count differs from `eng/test-counts.json`, which a PR that adds tests updates, and if branch or line
coverage of `src/Resultful` falls below `eng/coverage-floor.json`. `ReferenceModel.cs` in the consumption suite is
a plain model of 0.2's results; the laws run the same operations on it and on the library.
