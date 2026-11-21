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
   Give each operation its own result type (`PlaceOrderResult` instead of `Result<Order, OrderError>`) with
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

1. **The base constructor:** `private protected OrderError(string message) : base(message) { }` (when no
   matching constructor is hand-written), so the ordinary way to derive is closed to other assemblies. It doesn't
   close the hierarchy on its own: the copy constructor of a non-sealed record must be `public` or `protected` (CS8878), and another assembly derived
through it (**verified**). The guard is the analyzer rule that reports a derived
   type outside the base's assembly ([decision 15](../decisions/0015-analyzer-rules-read-attributes.md)). (Positional
   derived records don't cause CS0108: a parameter named like an inherited property reuses it.)
2. **Exhaustive `Match<TResult>` and `Switch`:** Generated across all concrete leaves in the assembly, including
   leaves under nested `[ClosedHierarchy]` bases (`OrderNotFound`, `OrderLocked`, `CardDeclined`). Adding a new
   leaf breaks every `Match`/`Switch` call site at compile time on every IDE and compiler version, without
   depending on `CS8509` suppression.
3. **Deterministic, AOT-safe boundary mapper (`TryMap`):** Walks `error.Causes()` outer-to-inner with a static
   `switch` over the hierarchy's leaves:

```csharp
OrderError.TryMap(
    wrappedError,
    onCardDeclined: (CardDeclined _) => 402,     // typed, as RF0008 requires: a reordered case won't compile
    onOrderLocked: (OrderLocked _) => 409,
    onOrderNotFound: (OrderNotFound _) => 404,
    out var status);
```

Because it walks `Causes()` outer-to-inner rather than calling `TryFind<T>` per dictionary entry, precedence is
deterministic when multiple errors in a cause chain are mappable, with no reflection.

## Tier 2: Narrow result shapes

C# has no library-level generic type aliases, and structs are invariant (`Result<Order, OrderError>` does not
implicitly convert to `Result<Order>` or `Result<Order, Error>`).

A user declares a result type for one operation, naming its success type and its error set:

```csharp
[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

[ResultShape(typeof(OrderError))]              // no Success: a step with no value
public readonly partial struct CancelOrderResult;
```

The first prototype generated a generic `OrderResult<T>`. Review found it misclassifies a success whose type is
also an error case (below), so [decision 16](../decisions/0016-source-generated-result-shapes.md) uses narrow,
non-generic shapes instead. From a declaration, the generator emits:

- `[MustObserve]` on the struct.
- State-byte storage (`0` = uninitialised, `1` = success, `2` = failure) plus an `[Obsolete(error: true)]`
  parameterless constructor, matching [decision 10](../decisions/0010-default-is-not-success.md).
- `IsSuccess`, `IsFailure`, `TryGetValue`, `TryGetError` (with `[MaybeNullWhen(false)]`), `Ok`, `Fail`, `Match`.
- `Map` and `Bind`, which return the core `Result<TOut, OrderError>`: a `PlaceOrderResult` holds only an `Order`.
- Implicit conversions from the success type and from each error leaf
  (`PlaceOrderResult r = new OrderLocked(7);`).
- **Widening out**, to `Result<Order, OrderError>` and `Result<Order>`.
- **Conversions in** (**verified** on a hand-written shape, next to the `[Union]` conversions with no ambiguity):
  - from `Result<Order, OrderError>`, so a pipeline of core steps returns straight into the shape;
  - from another shape with the same success type and error set (`PlaceOrderResult r = loadOrderResult;`);
  - from a different error hierarchy only through a mapping the user writes (design):
    `static partial OrderError From(PaymentError e);`.
- `WithContext(string)` that keeps the shape: context is a note on the error
  ([decision 14](../decisions/0014-result-and-validation.md)), so the result can still be switched over.

```csharp
Result<Order, OrderError> Reserve(Order o) => ...;

PlaceOrderResult Place(Cart c) =>
    Load(c.OrderId)                            // Result<Order, OrderError>
        .Bind(Reserve)
        .WithContext($"placing order {c.OrderId}");   // converts into PlaceOrderResult on return

Result<Order> widened = Place(cart);           // widening out at a layer boundary
```

### The overlap rule

A union switch matches by type, so the success type must not be something an error case could also be.

```csharp
// the first prototype's generic shape
OrderResult<OrderNotFound> a = OrderResult<OrderNotFound>.Ok(new OrderNotFound(1));
var r = a switch { OrderNotFound => "...", OrderLocked => "...", CardDeclined => "..." };
// compiled with no warning under TreatWarningsAsErrors; threw SwitchExpressionException at runtime
```

The compiler matched `OrderNotFound` through the leaf's `TryGetValue`, which only accepts failures, so the
success matched no arm (**verified**). `OrderResult<object>` also needed a `null` arm (CS8655). A narrow shape
names its success type where it's declared, so the generator reports an error there if `Success` is a leaf, a base
of a leaf, `Error`, `object`, or an interface a leaf implements. No use-site analyzer is needed.

### .NET 11 `[Union]` integration — replacing `OneOfBase` with a native struct union

`OneOf` tried to solve domain-named results with `OneOfBase<T0, T1, ...>`, which forced heap-allocated `class`
inheritance, constructor boilerplate, and `.Match(...)` lambdas because C# couldn't exhaustively `switch` over it.

Because a shape is a `readonly partial struct` and the generator already knows every leaf of a
`[ClosedHierarchy]` error, on `net11.0` (`#if NET11_0_OR_GREATER`) it emits `[System.Runtime.CompilerServices.Union]`,
`IUnion`, `HasValue`, `public object? Value`, and one `public` union constructor + non-boxing `TryGetValue(out Leaf)`
per concrete leaf (keeping the constructor from `OrderError` itself `private` so the base isn't an extra unmatched
union case).

That makes the shape a **native C# 15 union** with flat, exhaustive `switch` expressions: no `_` arm, no
`OneOfBase` class, no `.Match(...)`, and no `CS8509` suppressor.

```csharp
// Flat: the success type and every closed error leaf
var status = Place(cart) switch
{
    Order o => Created(o),
    OrderNotFound nf => NotFound(nf.Id),
    OrderLocked => Conflict(),
    CardDeclined => PaymentRequired(),
};

// Grouped: an intermediate base (OrderPaymentError) covers its leaves
var grouped = Place(cart) switch
{
    Order o => 201,
    OrderNotFound => 404,
    OrderLocked => 409,
    OrderPaymentError => 402,
};
```

**Verified**: both forms on the generic prototype, and the flat form on a hand-written narrow shape. Removing an
arm gives CS8509 from the compiler (negative control run for the flat form on both). Adding a leaf to `OrderError`
adds a union case, so every `switch` over the shape warns until it handles it. On `netstandard2.0` and `net10.0`,
the same declaration falls back to the generated `Match`/`Switch` methods.

More broadly, this generator pattern can scaffold custom `[Union]` structs as a value-type successor to
`OneOf.SourceGenerator` / `OneOfBase`. Native C# 15 unions make `OneOf` largely unnecessary on `net11.0`; the
generator's lasting value is one declaration that works from `netstandard2.0` to `net11.0`, plus the railway
surface, the conversions, and the `Causes()` boundary mappers that raw unions don't provide.

### Shapes that gather: `ManyErrors`

`ManyErrors = true` makes the named form of `Validation<T, TError>`, as a plain shape is the named form of
`Result<T, TError>`. The maintainer chose this over moving `Validation` out of the core: the generator lands in
M4, M2's accumulating operators need a type now, and `Combine` has to take inputs with different success types,
which C# can't infer through a generic constraint over generated shapes. So the gathering steps use the core
`Validation`, and the endpoint names the operation:

```csharp
[ResultShape(typeof(InputError), Success = typeof(Registration), ManyErrors = true)]
public readonly partial struct RegisterResult;

RegisterResult Register(Request r) =>
    Validation.Combine(Name(r.Name), Email(r.Email), Age(r.Age), (n, e, a) => new Registration(n, e, a));

var response = Register(request) switch
{
    Registration reg => Created(reg),
    IReadOnlyList<InputError> errors => ValidationProblem(errors.Select(e => e.Match(…))),
};
```

The switch isn't flat over the leaves, because one failure holds many. The two arms are checked by the compiler.
Each error inside is handled with the generated `Match`, or a `switch` that the analyzer's suppression covers;
the compiler alone reports CS8509 for a `switch` over the closed base (**verified**).

## The core and the generator

Two questions decide where a piece lives:

| Question | If yes |
|---|---|
| Does it depend on the user's list of types? | **Generated**: case constructors, conversions, `Match` and `TryMap` parameters, `[Union]` members (`TryGetValue` per case, `Value`), the `private protected` constructor, the overlap and `partial` diagnostics |
| Must it exist without the generator, or before M4? | **Core**: `Result`, `Validation`, `Unit`, `Error`, `ValidationFailed`, the attributes, and any behaviour that's the same for every shape |

A shape is a named wrapper over one core value:

| Declaration | Wraps | Success arm | Failure arms |
|---|---|---|---|
| `Success = typeof(Order)` | `Result<Order, OrderError>` | `Order` | each leaf |
| no `Success` | `Result<Unit, OrderError>` | `Unit` | each leaf |
| `Success = typeof(Registration), ManyErrors = true` | `Validation<Registration, InputError>` | `Registration` | `IReadOnlyList<InputError>` |
| no `Success`, `ManyErrors = true` | `Validation<Unit, InputError>` | `Unit` | `IReadOnlyList<InputError>` |

What follows:

- **Behaviour is written once.** `Map`, `Bind`, `WithContext`, widening and the zero-value guard live on the core
  type; generated members forward to the wrapped value. The struct is the same size as the core value.
- **Widening is free.** `AsResult()` returns the wrapped value, and the implicit conversion does the same.
- **Generated code is thin.** It holds only what the user's types decide, so a generator bug can't change how
  `Bind` behaves.
- **The generator depends on the core.** Generated code calls core types, so `Resultful.Generators` requires
  `Resultful`. The analyzers don't: a library can still declare its own attribute copy and get the checks.
- **Extension methods don't reach a shape.** Generic inference ignores user-defined conversions, so a core
  extension on `Result<T, TError>` doesn't bind to `PlaceOrderResult`, and the async extensions don't bind to
  `Task<PlaceOrderResult>`. The generator forwards the common instance members (`Map`, `Bind`, `Match`,
  `TryGetValue`, `TryGetError`, `WithContext`, `AsResult`); the rest, and async, are open below. An extension on
  an interface the shape implements does bind, at the cost of a box
  ([Building the generator](building-the-generator.md#assembly-size)).

**Verified** on SDK 11.0.100 (`temp/gen-check/Consumer/WrapProbe.cs.txt`, with stand-in core types that have the
decision 10 guard): `[Union]` structs wrapping a core `Result<Order, OrderError>`, `Result<Unit, OrderError>` and
`Validation<Order, OrderError>` each switch exhaustively with no `_` arm. Removing the `CardDeclined` arm, the
`Unit` arm or the list arm each gives CS8509, and `default` of a shape throws through the wrapped value. The real
core `Result` doesn't have the guard yet; it arrives with decision 10.

### Opt-in shape knobs (design)

Not prototyped yet, but natural extensions on `[ResultShape]`:

- `Async = true` (emits `Task`/`ValueTask` extension methods).
- `Operators = true` (emits opt-in operators in a nested or sibling `Operators` namespace).
- `JsonConverter = true` (emits an AOT-safe `JsonConverterFactory` — see the STJ finding below).

## Verified probe results (SDK 11.0.100)

The prototype lives in `temp/gen-probe/` (gitignored) on the branch where this exploration started, and was built
and executed with `TreatWarningsAsErrors=true` and `EnforceExtendedAnalyzerRules=true`. It generates the generic
`OrderResult<T>` that came first. The review rows at the end were run on a copy of it (`temp/gen-check/`), with the
narrow shape written by hand from the generated code, since the generator doesn't emit narrow shapes yet.

| Check | Result |
|---|---|
| Tier 1 `[ClosedHierarchy]` `private protected` ctor + nested hierarchy (`OrderPaymentError : OrderError`) | **Verified** — compiles with zero warnings (no `CS0108`, no `CS8509`) |
| Tier 1 exhaustive `Match` across 3 leaves (`OrderNotFound`, `OrderLocked`, `CardDeclined`) | **Verified** — runs and returns 404 |
| Tier 1 `OrderError.TryMap` over a `WithContext`-wrapped error | **Verified** — walks `Causes()` outer-to-inner and finds `OrderNotFound` |
| Tier 2 `[ResultShape(typeof(OrderError))]` implicit from leaf, widening to `Result<int>`, `WithContext` + `TryFind` | **Verified** — all assertions pass |
| Tier 2 `.NET 11 [Union]` flat exhaustive `switch` over `T` + every closed leaf (`OrderNotFound`, `OrderLocked`, `CardDeclined`) and grouped intermediate base (`OrderPaymentError`) | **Verified** — compiles with zero warnings (no `_` arm, no suppressor) and runs |
| Decision 10 zero-value guard on generated `OrderResult<int>` | **Verified** — `default(OrderResult<int>).IsSuccess` throws `InvalidOperationException` |
| `System.Text.Json` source generation (`[JsonSerializable(typeof(OrderResult<int>))]`) in the same compilation | **Verified failure (see below)** — `CS0619` and 0 properties discovered (not re-run in review) |
| Review: rebuilt the probe; removed the `CardDeclined` arm from the flat union switch | **Verified** — `CS8509` from the compiler, so the exhaustiveness is real |
| Review: `OrderResult<OrderNotFound>` holding a success, switched over its leaves | **Verified failure** — no warning at build; `SwitchExpressionException` at runtime. The reason for narrow shapes and the overlap rule |
| Review: `OrderResult<object>` | Needs a `null` arm (`CS8655`) |
| Review: hand-written narrow `PlaceOrderResult` with `[Union]` | **Verified** — flat switch over `Order` and every leaf runs; removing an arm gives `CS8509` |
| Review: conversions into `PlaceOrderResult` from `Result<Order, OrderError>` and from `LoadOrderResult` | **Verified** — compile next to the union conversions with no ambiguity, and classify correctly |
| Review: accessibility of a positional `abstract record` constructor | `protected`, so another assembly can call it |
| Review: another assembly deriving from a base whose constructor is `private protected` | **Compiles**, through the record's synthesized `protected` copy constructor; declaring that `private protected` is CS8878. Constructors can't close a record hierarchy |
| Structure: `[Union]` shapes wrapping a core value (`Result<T,E>`, `Result<Unit,E>`, `Validation<T,E>`) | **Verified**: exhaustive with no `_`; removing a leaf, the `Unit` arm or the list arm gives `CS8509`; `default` throws |
| Structure: `switch` over each `OrderError` inside the list, without the analyzer | `CS8509` from the compiler; the generated `Match` compiles clean |

## Key constraint: generator-to-generator blindness

Roslyn runs source generators in parallel over the user's original syntax/compilation; one generator cannot see
members emitted by another generator in the same compilation.

When `[JsonSerializable(typeof(OrderResult<int>))]` (the generic prototype) was placed in the same project as `[ResultShape]`,
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
     widening conversion `PlaceOrderResult -> Result<Order>`), keeping shapes internal to the domain/application
     layer.

## Costs and boundaries

- **Incremental pipeline hygiene** (specified in [Building the generator](building-the-generator.md)): The generator must extract value-equatable models (`ClosedHierarchyModel`,
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

Decided in [decision 16](../decisions/0016-source-generated-result-shapes.md): a separate `Resultful.Generators`
package, both attributes in `Resultful.CodeAnalysis` in the core, narrow shapes, void shapes succeeding with `Unit`,
and `ManyErrors`. The split between the core and the generator above is proposed.

- **Members a shape forwards, and async:** which core extensions the generator repeats on a shape, and whether
  async callers `await` to a core `Result` and convert, or the generator emits `Task<Shape>` extensions.
- **The list type in a gathering shape:** `IReadOnlyList<TError>` as the case type, or a generated wrapper
  (`RegisterErrors`) that can carry its own `Match`.
- **Mapping between error hierarchies:** the `static partial OrderError From(PaymentError e)` form is design only.
- **Conversions between shapes with nested error sets:** from a shape whose error type is a closed base nested
  under this one. Only the same error type was run.
- **STJ / AspNetCore integration:** whether shapes emit optional `JsonConverter` / `IResult` helpers when those
  assemblies are referenced, or rely on widening to `Result<T>`.
