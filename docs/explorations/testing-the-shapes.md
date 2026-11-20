---
type: exploration
title: "Testing the shapes"
description: "The target state for generated result shapes and the core they stand on, written as a list of behaviours with IDs, and the layers of tests that prove each one: the generator's output, the shapes run against the real core, compile-time outcomes, packaging, mutation and coverage."
status: draft
tags: [resultful, exploration, source-generators, testing]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Testing the shapes

> Draft exploration behind [decision 16](../decisions/0016-source-generated-result-shapes.md). It sets the target
> for the generator and the core together, and the tests that prove it.
> [Building the generator](building-the-generator.md) checks what the generator *emits*; this page checks what the
> emitted code *does*. Claims marked **verified** were compiled on SDK 11.0.100. Everything else is design.

## Why a second page

The generator experiment (`rtkelly13/generator-experiment`, PR #37) follows *Building the generator* closely:
the six diagnostics, the fixtures, rules on syntax and symbols, budgets and incremental steps. Its tests pass on
`net10.0` and `net11.0`. But they stop at the output:

- **No test runs generated code.** The tests compile each fixture and inspect the result. None of them calls
  `Ok`, `Match` or a conversion and checks the answer.
- **The core is a stub.** The tests compile against `CoreStubSource`, a hand-written `Result`, `Validation`,
  `Error` and `Unit`. The shipped core is different. Its `Result<T, TError>` wraps `OneOf` and has no zero-value
  guard, `TryGetValue`, `TryGetError` or `AsResult()`. There's no `Validation`, and `Unit` is a class where the
  stub has a struct. So a green build shows the shapes work with the stub, not with Resultful.

So the two things that matter most aren't tested yet: whether a shape behaves correctly, and whether it builds
against the core that ships.

## The target state

### The core a shape stands on

A shape wraps one core value ([decision 16](../decisions/0016-source-generated-result-shapes.md) item 8). This
is what the core must provide, against what ships today:

| Core member | Needed for | Ships today | Arrives with |
|---|---|---|---|
| `Result<T, TError>` whose zero value throws on every read (`IsDefault`) | the shape's zero-value guard | no: `default` is a success | [decision 10](../decisions/0010-default-is-not-success.md), M3 |
| `TryGetValue`, `TryGetError` | `Match`, `[Union]` members | no | M3 |
| `Map`, `Bind`, `Match` | forwarded members | yes, over `OneOf` | — |
| `AsResult()` to `Result<T>` when `TError : Error` | widening | no | [decision 14](../decisions/0014-result-and-validation.md), M2 |
| `Validation<T, TError>` with `TryGetErrors`, `Map`, `Match`, `Combine` | `ManyErrors` shapes | no | decision 14, M2 |
| `Error` with `Cause`, `Causes()`, `WithContext` that keeps the type | `TryMap`, context on a shape | no | decision 14, M2 |
| `Unit` | valueless shapes | yes (a class) | — |
| `[MustObserve]`, `[ClosedHierarchy]`, `[ResultShape]` | what the generator reads | no | [decision 15](../decisions/0015-analyzer-rules-read-attributes.md), M3 |
| `IResultShape<T, TError>`, `IValidationShape<T, TError>` | satellites that take any shape | no | M4, with the generator |

Until those land, the generator's tests need a stand-in core. The rule for the stand-in is under
[Against the real core](#2-against-the-real-core).

### What a shape does

Each behaviour has an ID. Tests carry it as `[Trait("Capability", "SHAPE-MATCH")]`, and a gate checks the list
both ways: every ID has a test, and every test names an ID that's on this page.

| ID | Behaviour |
|---|---|
| `SHAPE-CREATE` | `Ok(v)`, `Fail(e)`, and implicit conversions from the success type and from every leaf, build the right case. On .NET 11, one `public` union constructor per leaf does the same. |
| `SHAPE-ZERO` | `default(PlaceOrderResult)` throws on every read: `TryGetValue`, `TryGetError`, `Match`, `switch`, `AsResult`, widening. `HasValue` is `false` and doesn't throw. |
| `SHAPE-SWITCH` | On .NET 11, a `switch` over the shape is exhaustive with every leaf (flat) or every intermediate base (grouped), with no `_` arm. Removing an arm gives CS8509. |
| `SHAPE-MATCH` | `Match` calls exactly one arm, once: `onSuccess` for a success, otherwise the arm of the error's most-derived leaf. |
| `SHAPE-TRYGET` | `TryGetValue(out T)` and `TryGetError(out TError)` agree with the case. On .NET 11, `TryGetValue(out Leaf)` is `true` only for that leaf. |
| `SHAPE-WIDEN` | `AsResult()`, the implicit conversion to `Result<T, TError>` and the one to `Result<T>` keep the value or the same error instance. A round trip through the core returns an equal shape. |
| `SHAPE-INTO` | A `Result<T, TError>` with the same types converts into the shape. So does another shape with the same success type and the same error type. A different error hierarchy doesn't convert without a `From` mapping. |
| `SHAPE-COMPOSE` | `Map` and `Bind` on a shape give the same result as on `AsResult()`. |
| `SHAPE-CONTEXT` | `WithContext` on a failed shape returns the same shape type, and the error keeps its leaf type. |
| `SHAPE-UNIT` | A shape with no `Success` succeeds with `Unit`, and its `switch` has a `Unit` arm. |
| `SHAPE-GATHER` | A `ManyErrors` shape fails with a non-empty list and switches over two arms. `Combine` gathers every error. `Validation<T, TError>` converts into it. |
| `SHAPE-NOALLOC` | Building, switching, `Match` with static lambdas, `TryGetValue` and widening allocate nothing. Only `Value` boxes. |
| `HIER-CLOSE` | A `partial` `[ClosedHierarchy]` base gets a `private protected` constructor. |
| `HIER-MATCH` | A base's generated `Match` and `Switch` reach every leaf, most-derived first. |
| `HIER-TRYMAP` | `TryMap` walks `Causes()` from the outer error inwards and returns the first match. |
| `GEN-DIAG` | `RFG0001`–`RFG0006` are reported where *Building the generator* says, and nowhere else. |

Not in the target yet: async on a shape, JSON converters, and `From` mappings between hierarchies. Each one gets
an ID when it's designed.

## How it's tested

Seven layers. Each one catches something the layer before it can't.

### 1. The generator's output

This is PR #37, built to [Building the generator](building-the-generator.md): diagnostics, rules on syntax and
symbols, member and IL budgets, incremental steps.

- **Catches:** wrong declarations, unqualified names, a missing `[MustObserve]`, cases in the wrong order, code that
  grows, a pipeline that stops caching.
- **Can't catch:** wrong behaviour. If the code is the right shape but takes the wrong branch, every test still
  passes.

### 2. Against the real core

The fixtures compile against the built `Resultful.dll`, referenced from the project, and not against a source
stub. Until the core has the members in the table above, the stub stays, with two rules:

- **One stub, checked against the plan.** The stub is the only copy, and a test compares its public surface with
  the core's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. While the core catches up, a missing member
  is listed in an allowlist file. A new gap fails the build. When the core adds a member, its line comes out of
  the allowlist. When the allowlist is empty, the stub is deleted.
- **The stub matches the core where both exist.** For example, `Unit` is a class in both.

- **Catches:** shapes that only build against the stub.

### 3. Shapes run as code

A new project, `tests/Resultful.Generators.Behaviour.Tests`, targets `net10.0;net11.0`. It references the
generator as an analyzer (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`) and the core as a normal
reference. Its fixtures are ordinary source files: the nine from *Building the generator*, compiled by the build.
Its tests call the shapes the way an application would.

- **Example tests** for each ID, written out by hand: one success, one failure for each leaf, and the zero value.
- **Property tests** with a property-testing library (CsCheck or FsCheck, new to the repo). The main one is
  differential: for any value and any leaf, each generated member gives the same answer as the core member it
  stands for.
  - `shape.Map(f)` equals `shape.AsResult().Map(f)`; the same for `Bind`.
  - `Match` gives the same result as a hand-written `switch` over the core value, and calls exactly one arm.
  - `(PlaceOrderResult)(Result<Order, OrderError>)shape` equals `shape`.

  A generator bug can't fake this property. The generated code has to agree with the core for every value.
- **Allocation tests** for `SHAPE-NOALLOC`. Measure `GC.GetAllocatedBytesForCurrentThread()` around a loop of
  warmed-up calls and assert the difference is zero. This runs in an ordinary test, with no benchmark harness.
- **Both targets.** On `net11.0` the `switch` tests run through `[Union]`. On `net10.0` the same IDs run through
  `Match`. A test that only exists on one target is marked so in its trait.

- **Catches:** a wrong branch, a lost error instance, a broken guard, a forwarder that drifts from the core, a
  hidden box.

### 4. Compile-time outcomes

Some behaviours *are* compiler errors: a missing arm, an overlapping success type, a conversion that shouldn't
exist. These fixtures are compiled by the test harness, not the build. Each expected diagnostic is marked on the
line where it should appear:

```csharp
var status = result switch   // expect: CS8509
{
    Order o => 201,
    OrderNotFound => 404,
};

PlaceOrderResult r = paymentResult;   // expect: CS0029
```

The runner compiles each fixture and compares the diagnostics it gets with the markers, both ways: a missing
diagnostic fails, and so does an extra one. This covers `SHAPE-SWITCH`, `SHAPE-INTO` (the conversions that must
not compile) and `GEN-DIAG`, and replaces the per-test asserts on diagnostic IDs in PR #37.

- **Catches:** a conversion that compiles when it shouldn't, an exhaustive `switch` that stops being checked, a
  diagnostic that moves to the wrong line.

### 5. Across assemblies and from the package

- **Cross-assembly:** the leaves live in one project, the shape in another, and the layer 3 tests run against
  the shape. This is the case where the generator reads metadata rather than source.
- **From the package:** CI packs `Resultful` and `Resultful.Generators` into `artifacts/` and builds a sample
  with `PackageReference`s only, restoring from that folder. PR #36 already does this for the core. It catches a
  generator that sits in the wrong folder of the package, or a missing dependency.
- **AOT:** the sample is published with `PublishAot=true` and run on CI, from M5. Any trimming or AOT warning
  fails.

### 6. Measuring the tests

Two measures that show where the tests are thin.

- **Every generated member is called.** A Roslyn walk over the layer 3 tests lists each fixture shape's public
  members that a test calls. It's the same walk that PR #36's `PublicApiCoverageTests` does over the core's
  `PublicAPI` files. Any generated member that no test calls fails the gate. This doesn't depend on whether
  coverage tools count generated files.
- **Mutation testing,** scheduled rather than on every PR. Stryker.NET mutates the generator's own source,
  including the strings it emits. So a flipped `!_inner.IsDefault` or a swapped case order becomes a mutant, and
  layers 1 to 4 must kill it. Surviving mutants are a list of behaviours with no test. Stryker's runner for
  Microsoft.Testing.Platform is in preview, and whether it works with xunit.v3 4.0 hasn't been checked. If it
  doesn't, the first run uses a VSTest project.

For the core itself, the matching measure is branch coverage joined with its `PublicAPI` files (PR #36).

### 7. Samples

Three use cases from [Errors in practice](../errors-in-practice.md), rewritten with shapes and run as tests:

| Use case | Shape | Test |
|---|---|---|
| 1. Endpoint statuses | `PlaceOrderResult`, flat `switch` to status codes | `WebApplicationFactory`: one request per leaf, asserting the status |
| 2. A sign-up form | `RegisterResult` with `ManyErrors` | every invalid field reported at once |
| 6. A queue handler | a valueless shape and `TryMap` | the handler's decision (retry, dead-letter, complete) for each error |

The samples are what a reader copies. If a sample's code changes, its test fails before its documentation goes
stale.

## Which layer proves which behaviour

| ID | 1 Output | 3 Run | 4 Compile | 5 Package | 6 Measure |
|---|---|---|---|---|---|
| `SHAPE-CREATE` | members exist | ✓ | | ✓ | ✓ |
| `SHAPE-ZERO` | | ✓ | | | ✓ |
| `SHAPE-SWITCH` | union members | ✓ | ✓ CS8509 | | |
| `SHAPE-MATCH` | case order | ✓ property | | | ✓ |
| `SHAPE-TRYGET` | | ✓ | | | ✓ |
| `SHAPE-WIDEN` | | ✓ property | | | ✓ |
| `SHAPE-INTO` | | ✓ | ✓ CS0029 | | ✓ |
| `SHAPE-COMPOSE` | | ✓ property | | | ✓ |
| `SHAPE-CONTEXT` | | ✓ | | | |
| `SHAPE-UNIT` | | ✓ | ✓ | | |
| `SHAPE-GATHER` | | ✓ | ✓ | | ✓ |
| `SHAPE-NOALLOC` | no `box` in IL | ✓ allocation | | AOT | |
| `HIER-*` | budgets | ✓ | | | ✓ |
| `GEN-DIAG` | ✓ | | ✓ markers | | |

## Where PR #37 stands

Checked against the code at `7dc7f42`:

- **Matches the spec:** `RFG0001`–`RFG0006` with release tracking, the five tracking names, the nine fixtures,
  the syntax and symbol rules, incrementality with the check for Roslyn objects, and the IL gate. CI is green.
- **Layers 2 to 7 aren't there yet,** as above. It's against the stub core, and no generated code runs.
- **Conversions from errors:** the shape has one implicit conversion from the error base, not one per leaf. A
  user-defined conversion from a base type accepts any leaf, so the behaviour is the same with fewer members.
  Layer 3 tests that, and decision 16 item 4 should say "from the error type".
- **`WithContext` isn't forwarded,** because the stub has no `WithContext`. `SHAPE-CONTEXT` waits for the core.
- **Budgets are looser than measured in places:**
  - `ManyErrors` has one row, 19 members, while `net10.0` measures 10. Split it into `net10` and `net11` rows,
    as the narrow shape is.
  - `Match` and `TryMap` share one dispatch ceiling (`80 + 30L`), set from `TryMap`'s 63 bytes. `Match`
    measures `38 + 24L`, so it could double before the gate notices. Give each its own ceiling.
- **Not done from the spec:** the bare compile with `generated_code = false`, and the box benchmark that decides
  whether `Map` and `Bind` stay forwarded. Layer 3's allocation test can answer the second for the forwarded
  members. The interface extension still needs a benchmark.

## Order of work

1. **Now, on the experiment:** layer 3 against the stub (example, property and allocation tests), layer 4's
   markers, the capability traits and gate, and the budget fixes above.
2. **Now, on PR #36:** the generated-member walk in layer 6 reuses its symbol mapping.
3. **As the core lands (M2, M3):** layer 2. Each core PR removes lines from the stub allowlist, and the stub goes
   when the allowlist is empty.
4. **With the generator package (M4):** layer 5, layer 7, and the first Stryker run.
5. **M5 and M6:** the AOT publish, and `[Union]` emission held to
   [decision 3](../decisions/0003-unions-are-chosen-by-measurement.md)'s checks.
