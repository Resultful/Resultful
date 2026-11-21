---
type: index
title: "Decisions"
description: "Every decision about Resultful, dated, with why; one file each."
status: stable
tags: [resultful, decision]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Decisions

Each decision is one file: what was decided, when, by whom, and why. A later decision that changes one
says which. A new decision takes the next number. `proposed` decisions wait for the maintainer.

| # | Decision | Status | Date |
|---|---|---|---|
| 1 | [The core package is dependency-free; OneOf interop moves to `Resultful.OneOf`](0001-core-is-dependency-free.md) | stable | 20 November 2026 |
| 2 | [Targets: `netstandard2.0`, `net10.0` and `net11.0`](0002-targets-netstandard20-net10-net11.md) | stable | 20 November 2026 |
| 3 | [The .NET 11 union integration is chosen by experiment](0003-unions-are-chosen-by-measurement.md) | stable | 20 November 2026 |
| 4 | [Sequential composition is additive: `Then`, `Sequence` and `Check`](0004-sequential-composition-is-additive.md) | stable | 20 November 2026 |
| 5 | [The package shape is checked in CI](0005-package-shape-is-checked.md) | stable | 20 November 2026 |
| 6 | [Tests are a mix: NUnit examples, xunit.v3 property tests](0006-tests-are-a-mix.md) | proposed | 20 November 2026 |
| 7 | [CI follows rig's hardening, not its whole job graph](0007-ci-follows-rig.md) | proposed | 20 November 2026 |
| 8 | [The dependency-free release is `0.3.0-beta01`](0008-version-0-3-0.md) | proposed | 20 November 2026 |
| 9 | [Operators are opt-in sugar over named methods](0009-operators-are-opt-in-sugar.md) | stable | 20 November 2026 |
| 10 | [`default` is not a success](0010-default-is-not-success.md) | stable | 20 November 2026 |
| 11 | [A family of satellite packages around a dependency-free core](0011-package-family.md) | stable | 20 November 2026 |
| 12 | [Assertions live in `Resultful.Assertions`](0012-assertions-live-in-resultful-assertions.md) | stable | 20 November 2026 |
| 13 | [Repository layout: `src`, `tests`, `samples`, `benchmarks`](0013-repository-layout.md) | stable | 20 November 2026 |
| 18 | [Assertions for Resultful live in this repository](0018-resultful-assertions-live-here.md) (partly supersedes 12) | stable | 21 November 2026 |
| 19 | [`Resultful.FSharp` is thin interop with `FSharp.Core`](0019-fsharp-interop.md) | stable | 21 November 2026 |
| 20 | [`Resultful.HotChocolate` maps error cases to GraphQL error types](0020-hotchocolate-graphql.md) | stable | 21 November 2026 |
| 21 | [M5 ecosystem candidates and samples](0021-ecosystem-candidates.md) | proposed | 21 November 2026 |
