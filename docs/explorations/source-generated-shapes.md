---
type: exploration
title: "Source-generated error hierarchies and result shapes"
description: "Using a Roslyn incremental source generator (Tier 1 + Tier 2) to emit closed error hierarchy plumbing, AOT-safe boundary mappers, and domain-specific result shapes that widen cleanly to Resultful's core types."
status: draft
tags: [resultful, exploration, source-generators, errors]
generated:
  by: gemini-4-neon
  at: 2026-11-20
---

# Source-generated error hierarchies and result shapes

> Draft exploration complementing the error-shape and analyzer-attribute proposals (PRs #34 and #35). Claims marked
> **verified** were compiled and executed on SDK 11.0.100 (`TreatWarningsAsErrors=true`, `Nullable=enable`), with
> the incremental generator targeting `netstandard2.0` against `Microsoft.CodeAnalysis.CSharp` 4.14.0 and the
> consumer targeting `net11.0`. Everything else is design.

## The idea

Resultful's canonical core stays dependency-free and small (`Result<T, TError>`, `Validation<T, TError>`, `Error`).
On top of that core, an opt-in incremental source generator (`Resultful.Generators`, or bundled into
`Resultful.Analyzers` as a separate package per [decision 11](../decisions/0011-package-family.md)) can eliminate
two specific places where C#'s type system creates friction:

1. **Tier 1 — Closed error hierarchies and boundary mappers (`[ClosedHierarchy]`):**
   Enforce cross-assembly closure in `csc` itself (not just via an analyzer), avoid the `CS0108` positional-record
   inheritance trap, and emit exhaustive `Match`/`Switch` plus zero-reflection `Causes()` boundary mappers.
2. **Tier 2 — Domain-specific result shapes (`[ResultShape]`):**
   Give bounded contexts real typed result aliases (`OrderResult<T>` instead of `Result<T, OrderError>`) with
   opt-in surface area and automatic widening to `Result<T>` / `Result<T, Error>`.

Everything generated still projects cleanly to the core `Result<T, TError>` and `Error` types, so libraries and
satellites keep a single lingua franca.

## Tier 1: Closed error hierarchies and boundary mappers

A user declares an `abstract partial record` deriving from `Error`:

```csharp
[ClosedHierarchy]
public abstract partial record OrderError : Error;

public sealed record OrderNotFound(int Id) : OrderError($"Order {Id} not found");
public sealed record OrderLocked(int Id) : OrderError($"Order {Id} is locked");

[ClosedHierarchy]
public abstract partial record OrderPaymentError : OrderError;

public sealed record CardDeclined(string Reason) : OrderPaymentError($"Card declined: {Reason}");
```

From this, the generator emits (**verified**):

1. **Compiler-enforced closure:** `private protected OrderError(string message) : base(message) { }` (when no
   matching constructor is hand-written). No external assembly can derive from `OrderError` even if that consumer
   never installs `Resultful.Analyzers`, and derived records don't repeat `string Message` (avoiding `CS0108`).
2. **Exhaustive `Match<TResult>` and `Switch`:** Generated across all concrete leaves in the assembly, including
   leaves under nested `[ClosedHierarchy]` bases (`OrderNotFound`, `OrderLocked`, `CardDeclined`). Adding a new
   leaf breaks every `Match`/`Switch` call site at compile time on every IDE and compiler version, without
   depending on `CS8509` suppression.
3. **Deterministic, AOT-safe boundary mapper (`TryMap`):** Walks `error.Causes()` outer-to-inner with a static
   `switch` over the hierarchy's leaves:

```csharp
OrderError.TryMap(
    wrappedError,
    onCardDeclined: _ => 402,
    onOrderLocked: _ => 409,
    onOrderNotFound: _ => 404,
    out var status);
```

Because it walks `Causes()` outer-to-inner rather than calling `TryFind<T>` per dictionary entry, precedence is
deterministic when multiple errors in a cause chain are mappable, with zero reflection and zero allocations.

## Tier 2: Domain-specific result shapes

C# has no library-level generic type aliases, and structs are invariant (`Result<Order, OrderError>` does not
implicitly convert to `Result<Order>` or `Result<Order, Error>`).

A user declares a domain-specific result type:

```csharp
[ResultShape(typeof(OrderError))]
public readonly partial struct OrderResult<T>;
```

From this, the generator emits (**verified**):

- `[MustObserve]` on the struct.
- State-byte storage (`0` = uninitialised, `1` = success, `2` = failure) plus an `[Obsolete(error: true)]`
  parameterless constructor, matching [decision 10](../decisions/0010-default-is-not-success.md).
- `IsSuccess`, `IsFailure`, `TryGetValue`, `TryGetError` (with `[MaybeNullWhen(false)]`), `Ok`, `Fail`, `Map`,
  `Bind`, `Match`.
- Implicit conversions from `T` and `OrderError` (so `OrderResult<int> r = new OrderNotFound(42);` works).
- **Automatic widening** to `Result<T, OrderError>` and `Result<T>` (when `TError` derives from `Error`), plus
  `WithContext(string)` returning `Result<T>`:

```csharp
OrderResult<int> r = new OrderNotFound(42);
Result<int> widened = r;                                  // implicit widening
Result<int> wrapped = r.WithContext("loading order");     // widens cleanly to Result<T>
```

### .NET 11 `[Union]` integration — replacing `OneOfBase` with a native struct union

`OneOf` tried to solve domain-named results with `OneOfBase<T0, T1, ...>`, which forced heap-allocated `class`
inheritance, constructor boilerplate, and `.Match(...)` lambdas because C# couldn't exhaustively `switch` over it.

Because a Tier 2 `[ResultShape]` is a `readonly partial struct` and the generator already knows every leaf of a
`[ClosedHierarchy]` error, on `net11.0` (`#if NET11_0_OR_GREATER`) it emits `[System.Runtime.CompilerServices.Union]`,
`IUnion`, `HasValue`, `public object? Value`, and one `public` union constructor + non-boxing `TryGetValue(out Leaf)`
per concrete leaf (keeping `OrderResult(OrderError)` `private` so `OrderError` itself isn't an extra unmatched
union case).

That turns `OrderResult<T>` into a **native C# 15 union** with flat, exhaustive `switch` expressions — no `_` arm,
no `OneOfBase` class, no `.Match(...)`, and no `CS8509` analyzer suppressor (**verified**):

```csharp
// Flat exhaustive switch over T and every closed error leaf
var status = orderResult switch
{
    int v => v,
    OrderNotFound nf => nf.Id == 42 ? 404 : 500,
    OrderLocked => 409,
    CardDeclined => 402,
};

// Grouping by an intermediate base (OrderPaymentError) is also exhaustive
var grouped = orderResult switch
{
    int v => v,
    OrderNotFound => 404,
    OrderLocked => 409,
    OrderPaymentError => 402,
};
```

Adding a new leaf to `OrderError` adds a new union case to `OrderResult<T>` automatically, so every `switch`
expression over `OrderResult<T>` immediately warns `CS8509` via the C# compiler itself. On `netstandard2.0` and
`net10.0`, the exact same `[ResultShape]` declaration falls back cleanly to the generated `Match`/`Switch` methods.

More broadly, this same generator pattern can scaffold any custom `[Union]` struct as a value-type successor to
`OneOf.SourceGenerator` / `OneOfBase`. While native C# 15 unions make `OneOf` itself largely unnecessary on
`net11.0`, the generator's lasting value is bridging `netstandard2.0`/`net10.0` to `net11.0` `[Union]` with one
declaration and adding the railway surface, widening conversions, and `Causes()` boundary mappers that raw unions
don't provide.

### Opt-in shape knobs (design)

Not prototyped yet, but natural extensions on `[ResultShape]`:

- `Cardinality = ResultErrors.One | ResultErrors.Many` (emits a `Validation`-shaped type with `IReadOnlyList<TError>`
  and `&`, omitting `Bind` unless requested).
- `Async = true` (emits `Task`/`ValueTask` extension methods).
- `Operators = true` (emits opt-in operators in a nested or sibling `Operators` namespace).
- `JsonConverter = true` (emits an AOT-safe `JsonConverterFactory` — see the STJ finding below).

## Verified probe results (SDK 11.0.100)

The prototype lives in `temp/gen-probe/` (gitignored) and was built and executed with `TreatWarningsAsErrors=true`
and `EnforceExtendedAnalyzerRules=true`:

| Check | Result |
|---|---|
| Tier 1 `[ClosedHierarchy]` `private protected` ctor + nested hierarchy (`OrderPaymentError : OrderError`) | **Verified** — compiles with zero warnings (no `CS0108`, no `CS8509`) |
| Tier 1 exhaustive `Match` across 3 leaves (`OrderNotFound`, `OrderLocked`, `CardDeclined`) | **Verified** — runs and returns 404 |
| Tier 1 `OrderError.TryMap` over a `WithContext`-wrapped error | **Verified** — walks `Causes()` outer-to-inner and finds `OrderNotFound` |
| Tier 2 `[ResultShape(typeof(OrderError))]` implicit from leaf, widening to `Result<int>`, `WithContext` + `TryFind` | **Verified** — all assertions pass |
| Tier 2 `.NET 11 [Union]` flat exhaustive `switch` over `T` + every closed leaf (`OrderNotFound`, `OrderLocked`, `CardDeclined`) and grouped intermediate base (`OrderPaymentError`) | **Verified** — compiles with zero warnings (no `_` arm, no suppressor) and runs |
| Decision 10 zero-value guard on generated `OrderResult<int>` | **Verified** — `default(OrderResult<int>).IsSuccess` throws `InvalidOperationException` |
| `System.Text.Json` source generation (`[JsonSerializable(typeof(OrderResult<int>))]`) in the same compilation | **Verified failure (see below)** — `CS0619` and 0 properties discovered |

## Key constraint: generator-to-generator blindness

Roslyn runs source generators in parallel over the user's original syntax/compilation; one generator cannot see
members emitted by another generator in the same compilation.

When `[JsonSerializable(typeof(OrderResult<int>))]` was placed in the same project as `[ResultShape]`,
`System.Text.Json.SourceGeneration` (**verified**):

1. Saw **0 properties** on `OrderResult<int>` (`new JsonPropertyInfo[0]` and an empty `{}` serializer).
2. Did not see the generated `[Obsolete(error: true)]` parameterless constructor, so it emitted
   `ObjectCreator = () => new OrderResult<int>()`, which then failed compilation with `CS0619`.

The same constraint applies to ASP.NET Core's Minimal API `RequestDelegateGenerator` if it inspects generated
members directly in the same compilation.

**Mitigations:**
- **Tier 1 (`[ClosedHierarchy]`) is unaffected** for ordinary serialization because user error records declare
  their own properties in source.
- **Tier 2 (`[ResultShape]`)** works cleanly with NativeAOT and `System.Text.Json` in two ways:
  1. Resultful's generator (or `Resultful.Text.Json`) emits a `JsonConverterFactory` / `[JsonConverter]` directly
     for the generated shape, or
  2. Consumers serialize/expose DTOs or the core `Result<T>` at the HTTP/JSON boundary (using the generated
     widening conversion `OrderResult<T> -> Result<T>`), keeping `OrderResult<T>` internal to the domain/application
     layer.

## Costs and boundaries

- **Incremental pipeline hygiene:** The generator must extract value-equatable models (`ClosedHierarchyModel`,
  `ResultShapeModel`) inside `ForAttributeWithMetadataName` and never cache `ISymbol` or `SyntaxNode`, so IDE
  typing stays fast.
- **Assembly-wide scan for Tier 1:** Finding all derived types of a `[ClosedHierarchy]` base touches compilation
  symbols; in production, derive the leaf list from a syntax-filtered `CompilationProvider` / type-declaration
  provider so unrelated edits don't invalidate cached output.
- **Library boundaries:** Custom `[ResultShape]` types are best suited to applications and bounded contexts. Public
  NuGet libraries should still expose `Result<T, TError>` / `Validation<T, TError>` (or provide the generated
  implicit conversion to them).
- **Native .NET 11 unions ([decision 3](../decisions/0003-unions-are-chosen-by-measurement.md)):** Even when C#
  gains native unions, Tier 1's `Causes()` boundary mappers and Tier 2's domain-result widening and railway methods
  remain useful; only the internal storage layout would simplify.

## Still open

- **Packaging:** Does the generator live in `Resultful.Analyzers` (one build-time package for both rules and
  generation) or a separate `Resultful.Generators` package?
- **Attribute home:** `[ClosedHierarchy]` is already proposed for `Resultful.CodeAnalysis` in the core (PR #35).
  Should `[ResultShape]` live there too, or only be added when Tier 2 ships?
- **Validation shapes:** Should `[ResultShape]` support `Errors = ResultErrors.Many` (generating a `Validation`-like
  struct) and/or `Void` shapes (`OrderResult` with no `T`)?
- **STJ / AspNetCore integration:** Whether Tier 2 emits optional `JsonConverter` / `IResult` helpers when those
  assemblies are referenced, or relies on widening to `Result<T>`.
