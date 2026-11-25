---
type: decision
title: "Source-generated error hierarchies and union-backed result shapes"
description: "An opt-in incremental source generator (Resultful.Generators) emits closed error hierarchy plumbing and narrow, operation-named [ResultShape] structs that lower to native .NET 11 [Union] types for flat, exhaustive switching."
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

The core consumer experience Resultful targets is a stack-allocated result struct, named after the operation,
that switches exhaustively over its success and every closed error case with no boilerplate:

```csharp
[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

var status = placeOrder(cart) switch
{
    Order o => Created(o),
    OrderNotFound nf => NotFound(nf.Id),
    OrderLocked => Conflict(),
    CardDeclined => PaymentRequired(),
};
```

To make this the centrepiece of the library while keeping the core dependency-free and cross-target:

1. **Attributes live in the core** (`Resultful.CodeAnalysis`), matched by full name: `[ClosedHierarchy]` and
   `[ResultShape(typeof(TError), Success = typeof(T), ManyErrors = false)]`
   ([decision 15](0015-analyzer-rules-read-attributes.md)).
2. **Generation lives in an opt-in package, `Resultful.Generators`** (Roslyn incremental generator targeting
   `netstandard2.0`, build-time development dependency only).
3. **Tier 1 (`[ClosedHierarchy]` on `abstract partial record`):** emits a `private protected` constructor,
   exhaustive `Match`/`Switch` across nested hierarchies, and a deterministic outer-to-inner `Causes()` boundary
   mapper (`TryMap`). The constructor closes the ordinary way to derive, but not the hierarchy: the copy constructor of a non-sealed record must be `public` or `protected` (CS8878), and another assembly derived through it (**verified**). Closure across assemblies is the analyzer's rule
   ([decision 15](0015-analyzer-rules-read-attributes.md)), and a foreign case still fails a `switch` at run time.
4. **Tier 2 shapes are narrow, not generic.** A `[ResultShape]` names one success type with `Success = typeof(T)`,
   or none for a step with no value (`[ResultShape(typeof(OrderError))] partial struct CancelOrderResult;`), whose
   success case is `Unit` ([decision 14](0014-result-and-validation.md)). The generated `readonly struct` has
   Decision 10's zero-value guard, `[MustObserve]`, implicit conversions from the
   success type and each error leaf, `TryGetValue`/`TryGetError`/`Match`, and implicit widening to
   `Result<T, TError>` and `Result<T>`. `WithContext` keeps the shape: context is a note on the error
   ([decision 14](0014-result-and-validation.md)), so the result can still be switched over. `Map` and `Bind`
   return the core `Result<TOut, TError>`, since the narrow shape holds only its own success type.
5. **Conversions into a shape are generated too**, so the core `Result` steps in the middle of a pipeline return
   straight into the narrow type:
   - from `Result<T, TError>` with the same success and error types (**verified**):
     `PlaceOrderResult Place(Cart c) => Load(c.Id).Bind(Reserve);`
   - from another shape with the same success type whose error type is the same or a closed base nested under this
     one (**verified** for the same error type): `PlaceOrderResult r = loadOrderResult;`
   - from a different error hierarchy only through a mapping the user writes, so no error changes meaning silently
     (design): `static partial OrderError From(PaymentError e);` on the shape, which the generator turns into a
     conversion from `PaymentResult`.

   C# applies one user-defined conversion at a time, so every conversion is emitted directly; none relies on
   chaining.
6. **A success type that overlaps an error case is a generator error.** If `Success` is a leaf, a base of a leaf,
   `Error`, `object`, or an interface a leaf implements (or, with `ManyErrors`, a type assignable to
   `IReadOnlyList<TError>`), a type pattern can't tell success from failure. In a
   generic `OrderResult<OrderNotFound>`, a success compiled with no warning and threw `SwitchExpressionException`
   at runtime (**verified**). A narrow shape names its success type when it's declared, so the generator reports
   it there and no use-site analyzer is needed.
7. **`ManyErrors = true` makes a shape that gathers**, the named form of `Validation<T, TError>` as the shape above is
   of `Result<T, TError>`: `[ResultShape(typeof(InputError), Success = typeof(Registration), ManyErrors = true)]
   partial struct RegisterResult;`. Its failure case is the non-empty `IReadOnlyList<InputError>`, so the switch has
   two arms, the success type and the list, and each error is handled inside it. The outer switch is exhaustive
   (**verified**). A `switch` over each `InputError` needs the analyzer's suppression or the generated `Match`; the
   compiler alone reports CS8509 there (**verified**). The steps that gather use the core `Validation` and its
   `Combine`, since C# can't infer a success type from a generic constraint over generated shapes; the conversion
   from `Validation<T, TError>` into the shape follows item 5 (design, not run).
8. **The core holds what works for every shape; the generator writes what depends on your types.** A shape is a
   named wrapper over one core value (`Result<T, TError>`, `Result<Unit, TError>` or `Validation<T, TError>`), so
   `Map`, `Bind`, async, widening and decision 10's guard are written once, in the core. The generator writes the
   case constructors, conversions, `Match` parameters, `[Union]` members and the diagnostics. A `[Union]` struct
   that wraps a core value still switches exhaustively, and its `default` throws when the core value's does
   (**verified** with stand-in core types). Details, and what the generator forwards, are in the exploration.
9. **.NET 11 `[Union]` scaffolding (`#if NET11_0_OR_GREATER`):** when `TError` is `[ClosedHierarchy]`, the
   generated struct emits `[System.Runtime.CompilerServices.Union]`, `IUnion`, `HasValue`, `public object? Value`,
   and one `public` union constructor and non-boxing `TryGetValue(out Leaf)` per concrete leaf. Flat switches
   (every leaf) and grouped switches (intermediate bases) are checked by `csc` itself with no `_` arm and no
   `CS8509` suppressor (**verified** for a generic and a hand-written narrow shape; removing an arm gives CS8509).
   On `netstandard2.0` and `net10.0`, the same declaration falls back to the generated `Match`/`Switch`.
10. **How the generator is built and checked** is specified in
   [Building the generator](../explorations/building-the-generator.md): the `RFG0001`–`RFG0006` diagnostics,
   cases ordered most-derived first (CS8510 otherwise, **verified**), a two-stage incremental pipeline that caches
   only plain data, a budget on generated members and IL, and checks on the emitted `.g.cs` without golden files.
   The generator experiment builds against that page.

Why:
- Replaces `OneOfBase`'s heap-allocated class inheritance and `.Match(...)` boilerplate with a stack-allocated
  struct that takes part in C# 15 `switch` expressions.
- Solves C#'s lack of library-level generic type aliases and struct invariance: `PlaceOrderResult` widens
  implicitly to `Result<Order>` when it crosses a layer boundary.
- A signature names the operation (`PlaceOrderResult Place(Cart cart)`), and the overlap hazard is caught where
  the type is declared.
- Verified on SDK `11.0.100` (`TreatWarningsAsErrors=true`) in
  [Source-generated error hierarchies and result shapes](../explorations/source-generated-shapes.md).

Amended the same day, by the maintainer's choice, after review: narrow shapes replace the generic
`OrderResult<T>` (which misclassified an overlapping `T`), the overlap rule became a generator error,
`WithContext` keeps the shape instead of widening, conversions into a shape were added, a valueless shape
succeeds with `Unit`, and `ManyErrors` was added. Item 8, the split between the core and the generator, is
proposed by the agent and awaits the maintainer.

Consequences:
- The core package stays dependency-free (`Resultful.Generators` is a separate opt-in package, per
  [decision 11](0011-package-family.md)).
- One type per operation that returns a shape. The steps between use the core `Result` and `Validation`.
- `TryGetValue(out var x)` compiles on `net10.0` but is CS0121 on `net11.0` for every shape: the `[Union]` members
  add one `TryGetValue` overload per case, so name the type (`TryGetValue(out Order? order)`).
- If item 8 is accepted: generated code calls the core, so `Resultful.Generators` requires the `Resultful` package. Declaring your own
  copy of an attribute without referencing Resultful ([decision 15](0015-analyzer-rules-read-attributes.md))
  works for the analyzers only.
- Whatever item 8 decides, core extension methods on `Result<T, TError>` don't bind to a shape: generic inference
  ignores user-defined conversions, so neither `placeOrderResult.Tap(…)` nor an `await` on `Task<PlaceOrderResult>`
  finds them. An extension on a core interface the shape implements (`this IResultShape<T, TError>`) does bind, but
  boxes the struct; a constrained generic gives CS0411 (both **verified**). The generator forwards `Map` and `Bind`
  until the box is measured; the async story is open.
- Roslyn source generators can't see code emitted by other generators in the same compilation (verified on the
  exploration's branch with `System.Text.Json.SourceGeneration`), so shapes either widen to `Result<T>` at
  JSON/HTTP boundaries or emit their own AOT-safe converters.
- The `[Union]` emission still goes through [decision 3](0003-unions-are-chosen-by-measurement.md)'s checks before
  it ships: a separate consuming assembly, identical and overlapping case types, null successes, boxing, and the
  benchmark.
- M4 (Tooling) adds `Resultful.Generators`, and M6 (Unions on .NET 11) includes `[Union]` emission on generated
  shapes alongside the core types.
