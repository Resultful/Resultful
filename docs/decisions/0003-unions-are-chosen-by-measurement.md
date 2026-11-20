---
type: decision
title: "The .NET 11 union integration is chosen by experiment"
description: "Candidates are checked for correctness, then benchmarked, before one is picked."
status: stable
tags: [resultful, decision]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 3. The .NET 11 union integration is chosen by experiment

The maintainer asked to explore unions for performance and fit rather than pick one up front. Candidates:

1. **`[Union]`/`IUnion` on the existing structs.** Exhaustive `switch` and case conversions for consumers.
   The cases are exposed through `object? Value`, which may box.
2. **Union-backed storage.** A compiler-generated `union` as the private field.
3. **Interop only.** Conversions to and from user-written unions, with the types themselves unchanged.

Correctness is checked first, from a separate consuming assembly under warnings as errors: overlapping case types
(`Result<T>` where `T` is `List<string>`, `string[]`, `IEnumerable<string>` or `object`, whose error case is
`IEnumerable<string>`), identical case types (`Result<string, string>`), null successes, and boxing. A candidate
that misclassifies a case is out, whatever it measures. The rest are compared with BenchmarkDotNet on throughput
and allocations, net10 against net11.

Already verified with SDK 11.0.100: `union R<T,E>(T, E);` lowers to a struct marked
`[System.Runtime.CompilerServices.Union]` implementing `IUnion`, and a hand-written `[Union]` struct gets
exhaustive `switch` and implicit conversions.

The outcome is a later decision that supersedes this one.
