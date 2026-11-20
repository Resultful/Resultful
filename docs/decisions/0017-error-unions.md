---
type: decision
title: "Error unions on .NET 11, alongside [ClosedHierarchy]"
description: "A C# 15 union of Error records works as an error type in Result and in a generated shape; [ClosedHierarchy] stays supported on every target."
status: proposed
tags: [resultful, decision, unions, errors]
decided:
  by:
  at:
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 17. Error unions on .NET 11, alongside `[ClosedHierarchy]`

**Proposed.** The design was agreed with the maintainer on 2026-11-20 and is written down here for review. Claims
marked **verified** were compiled and run on SDK 11.0.100 (`net11.0`) in the probes kept on the plan branch (PR #39):
`stack-plan/probes/union-error-probe`, `union-probe-2` and `union-probe-default`. The spec fixtures are
`tests/Resultful.Spec/Fixtures/UERR-*`, each with a `[ClosedHierarchy]` twin that must give the same output.

C# 15 lets an application write its error set as a union of records:

```csharp
public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");
public union OrderError(OrderNotFound, OrderLocked);
```

A `switch` over it is exhaustive, and dropping an arm gives CS8509 (**verified**). It compiles with the default
language version on `net11.0`, with no `preview` (**verified**). Resultful supports it **alongside**
`[ClosedHierarchy]`, which stays supported on every target ([decision 15](0015-analyzer-rules-read-attributes.md)).

## The seven points

1. **`Result<T, TError>` takes a union as it is.** It has no constraint on `TError`, so `Result<Order, OrderError>`
   works. A leaf doesn't convert straight into it: C# applies one user-defined conversion at a time, so
   leaf → union → result doesn't chain (CS0029, **verified**). Write `(OrderError)leaf`. This is documented, not a bug.
2. **`[ResultShape(typeof(OrderError))]` accepts a union.** The generator reads the union's cases as the leaf list and
   emits the same flat members as for a hierarchy ([decision 16](0016-source-generated-result-shapes.md)), including
   an implicit conversion from each leaf, so the shape's `switch` is flat.
3. **The union reaches the shared fields through `IErrorUnion`.** Every case derives from `Error`, so the leaves share
   `Message`, `Cause` and `Context`. The union itself is a struct and isn't an `Error`, so the core ships
   `IErrorUnion { Error Error { get; } }`. The generator adds it to any `partial union` whose cases are all errors,
   with `Message`, `Cause`, `WithContext` (which rewraps and keeps the union type) and `TryFind<T>` forwarding to the
   leaf. The interface on a union, and a generic constraint through it, are **verified**. Core helpers take
   `where TError : struct, IErrorUnion` overloads beside their `where TError : Error` ones, in a separate class,
   since overloads can't differ by constraint alone. (Probe 2 **verified** the pair with `where TError : struct, IUnion`;
   the `IErrorUnion` form is the same shape of overload and is design.) `IErrorUnion` ships on every
   target, so the public API is the same everywhere; only `net11.0` code can declare a union.
4. **`Fail` and the implicit conversion reject a union with no value** ([decision 10](0010-default-is-not-success.md)).
   `default(OrderError)` reaches a `switch` and throws `SwitchExpressionException` (**verified**), so it must not
   become a failure.
5. **An analyzer flags `default` of an error union** (RF0002).
6. **A shape's union cases must all derive from `Error`.** Otherwise the generator reports `RFG0007` on the shape.
7. **Multi-targeted code keeps `[ClosedHierarchy]`.** The reference app gets a `net11.0`-only folder that uses unions.

## A nested union is one case

`union OrderError(OrderNotFound, PaymentError)`, where `PaymentError` is itself a union, gives a shape two error
cases, `OrderNotFound` and `PaymentError`, not three. `Match` has a `PaymentError` parameter, there is a
`TryGetValue(out PaymentError)`, and a handler switches inside it for its leaves. The consumer grouped those errors
on purpose (generic handling for every payment failure), so the grouping is kept. Want flat? Declare a flat union.
This is the union counterpart of a grouped `switch` over an intermediate base (decision 16 item 9).

- A `switch` over a union holding a union can't be flat: an arm for an inner leaf is CS8121, a compile error, not a
  silent miss. `PaymentError p => p switch { … }` is exhaustive, and dropping an inner arm gives CS8509 on the inner
  switch (**verified**).
- The leaves of the nested union still convert into the shape implicitly, because the generator writes a conversion
  per leaf. A bare `Result<T, OrderError>` still needs `(OrderError)(PaymentError)leaf` (point 1).
- `IErrorUnion.Error` unwraps the inner union to the leaf:
  `Value switch { Error e => e, IErrorUnion u => u.Error, … }`. `WithContext` dispatches per direct case and rewraps,
  so the inner union's type is kept. (The probe's hand-written `Error` threw on a nested case; the generated one
  doesn't.)
- The JSON `type` and `IErrorUnion.Error` reach the innermost leaf.

## Widening

A shape whose error is a union widens implicitly to `Result<T>` through `IErrorUnion.Error`, so the `Result<T>` holds
the leaf, as it does for a hierarchy. `ErrorUnionExtensions.AsResult` does the same for a core `Result<T, TError>`.

## The zero check costs nothing

A compiler-generated union exposes only `object Value`, its constructors and `IUnion`; there is no `HasValue`
(**verified**). So `Fail` decides "no value" without boxing:

- A cached flag says whether `TError` is a union.
- When `TError` is a compiler-generated union whose one instance field is `object` (checked once per type by
  reflection and cached, together with `RuntimeHelpers.IsReferenceOrContainsReferences<TError>()`), and
  `Unsafe.SizeOf<TError>()` is pointer-sized, the field is read with `Unsafe.As<TError, object?>`. That allocated
  0 bytes per `Fail` (**verified**).
- Anything else that implements `IUnion`, such as a hand-written struct holding a `long`, takes the
  `IUnion { Value: null }` path (24 bytes, boxes) rather than being misread as a reference.
- A test pins the layout, so a compiler change fails loudly. This is `net11.0` only: `IUnion` doesn't exist below it.

## JSON

An error union writes as its leaf: `{"type":"OrderLocked","message":…,"context":[…],…}`. Reading resolves `type`
through the union's case list (the innermost leaves), with no reflection, so it stays AOT-safe; an unknown `type`
throws `JsonException`. Two leaves with the same simple name in one hierarchy or union would make `type`
ambiguous, so the generator reports `RFG0008`. This replaces the prefixing rule for colliding short names in
[Building the generator](../explorations/building-the-generator.md), which held only while names stayed in
parameter lists.

## Why

Unions are how .NET 11 code will write a closed set of errors, and the rest of Resultful already speaks in leaf
types. The smallest fit reuses the generator's leaf list and keeps decision 10's guarantee: one interface in the
core, one new generator diagnostic, and a zero check that costs nothing on the common path.

Alternatives rejected:

- **Flatten a nested union into its leaves.** Simpler shapes, but it throws away a grouping the consumer chose.
- **Unions only, dropping `[ClosedHierarchy]`.** `netstandard2.0` and `net10.0` have no unions.
- **`IErrorUnion` only on `net11.0`.** The public API files would differ by target for no gain.

## Consequences

- `IErrorUnion` ships in the core with the attributes (stack PR 07); `Fail`'s zero check with decision 10 (08b).
- The generator adds `RFG0007` and `RFG0008`, the union members and the nested-union unwrapping (13b). It also writes
  a `static IReadOnlyList<Type> Cases` on each `[ClosedHierarchy]` base and error union, the case list
  `Resultful.Text.Json` reads through, so the hierarchy's member budget in
  [Building the generator](../explorations/building-the-generator.md) goes from 3 to 4.
- `Resultful.Text.Json` reads unions and hierarchies through a registered case list (12b).
- RF0002 covers `default` of an error union (16a).
- Adding a case to a union, like adding a leaf to a hierarchy, changes a shape's `Match` signature: a binary break for
  callers in other assemblies.
