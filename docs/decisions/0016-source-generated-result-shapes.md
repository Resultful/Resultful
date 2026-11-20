---
type: decision
title: "Source-generated error hierarchies and union-backed result shapes"
description: "An opt-in incremental source generator (Resultful.Generators) emits closed error hierarchy plumbing and domain-specific [ResultShape] structs that lower to native .NET 11 [Union] types for flat, exhaustive switching."
status: stable
tags: [resultful, decision, source-generators, unions, errors]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: gemini-4-neon
  at: 2026-11-20
---

# 16. Source-generated error hierarchies and union-backed result shapes

The core consumer experience Resultful targets is a stack-allocated, domain-named result struct that switches
exhaustively over success and every closed error case with zero boilerplate:

```csharp
var status = orderResult switch
{
    int v => v,
    OrderNotFound nf => nf.Id == 42 ? 404 : 500,
    OrderLocked => 409,
    CardDeclined => 402,
};
```

To make this the centrepiece of the library while keeping the core dependency-free and cross-target:

1. **Attributes live in the core** (`Resultful.CodeAnalysis`), matched by full name: `[ClosedHierarchy]` and
   `[ResultShape(typeof(TError))]`.
2. **Generation lives in an opt-in package, `Resultful.Generators`** (Roslyn incremental generator targeting
   `netstandard2.0`, build-time development dependency only).
3. **Tier 1 (`[ClosedHierarchy]` on `abstract partial record`):** emits a `private protected` constructor (enforcing
   cross-assembly closure in `csc` and avoiding `CS0108`), exhaustive `Match`/`Switch` across nested hierarchies,
   and a deterministic outer-to-inner `Causes()` boundary mapper (`TryMap`).
4. **Tier 2 (`[ResultShape(typeof(TError))]` on `readonly partial struct`):** emits a `readonly struct` with
   Decision 10's zero-value guard, `[MustObserve]`, implicit conversions from `T` and error leaves, `Map`/`Bind`/
   `Match`/`TryGetValue`/`TryGetError`, and automatic widening to `Result<T>` / `Result<T, TError>` (including
   `WithContext`).
5. **`.NET 11 [Union]` scaffolding (`#if NET11_0_OR_GREATER`):** when `TError` is `[ClosedHierarchy]`, the
   generated `[ResultShape]` struct emits `[System.Runtime.CompilerServices.Union]`, `IUnion`, `HasValue`,
   `public object? Value`, and one `public` union constructor + non-boxing `TryGetValue(out Leaf)` per concrete leaf.
   Both flat switches (every leaf) and grouped switches (intermediate bases) are checked exhaustively by `csc`
   itself with no `_` arm and no `CS8509` suppressor. On `netstandard2.0` and `net10.0`, the same declaration falls
   back to generated `Match`/`Switch`.

Why:
- Replaces `OneOfBase`'s heap-allocated class inheritance and `.Match(...)` boilerplate with a stack-allocated
  struct that participates natively in C# 15 `switch` expressions.
- Solves C#'s lack of library-level generic type aliases and struct invariance (`OrderResult<T>` widens implicitly
  to `Result<T>` when crossing layer boundaries).
- Verified end-to-end on SDK `11.0.100` (`TreatWarningsAsErrors=true`) in
  [Source-generated error hierarchies and result shapes](../explorations/source-generated-shapes.md).

Consequences:
- The core package stays dependency-free (`Resultful.Generators` is a separate opt-in package, per
  [decision 11](0011-package-family.md)).
- Because Roslyn source generators cannot see code emitted by other generators in the same compilation (verified
  with `System.Text.Json.SourceGeneration`), Tier 2 shapes either widen to `Result<T>` at JSON/HTTP boundaries or
  emit their own AOT-safe converters.
- M4 (Tooling) adds `Resultful.Generators`, and M6 (Unions on .NET 11) includes `.NET 11 [Union]` emission on
  generated `[ResultShape]` structs alongside the core types ([decision 3](0003-unions-are-chosen-by-measurement.md)).
