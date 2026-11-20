---
type: exploration
title: "Building the generator"
description: "How Resultful.Generators is built: its diagnostics for deep and awkward hierarchies, an incremental pipeline that stays cached, a size budget for generated code, and checks on the emitted .g.cs with no golden files."
status: draft
tags: [resultful, exploration, source-generators, errors, testing]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Building the generator

> Draft exploration behind [decision 16](../decisions/0016-source-generated-result-shapes.md), written as the
> spec for the generator experiment that stacks on this branch. Claims marked **verified** were compiled on SDK
> 11.0.100 (`net11.0`, `Nullable=enable`). The pipeline and test patterns are taken from
> `Parquet.SourceGenerator`, where they run today. Everything else is design, and the experiment either
> confirms it or changes this page.

[Source-generated shapes](source-generated-shapes.md) says what the generator emits. This page says how it's built
and how its output is checked: which declarations it refuses, how it stays cheap in the IDE, how much code it may
emit, and the tests that keep all of that true. [Testing the shapes](testing-the-shapes.md) checks what the
emitted code does when it runs.

## Diagnostics

The generator reports problems where the type is declared. IDs use the `RFG` prefix, so they don't collide with
the analyzers' `RF` rules ([decision 15](../decisions/0015-analyzer-rules-read-attributes.md)). Each ID is tracked
in `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md`.

| ID | Severity | Reported when | Why |
|---|---|---|---|
| `RFG0001` | error | A shape's `Success` overlaps its errors: `Success` is `TError`, a leaf, a base of a leaf, a type a leaf derives from or implements, `Error` or `object`. With `ManyErrors = true`, also any type that `IReadOnlyList<TError>` converts to, or that converts to it. | A type pattern can't tell success from failure. The overlap is checked in both directions. |
| `RFG0002` | warning | A `[ClosedHierarchy]` contains a concrete type that isn't `sealed`. | The type is both a case and a base. Another assembly could derive from it. The generator still emits code and orders cases most-derived first. |
| `RFG0003` | error | A type that derives from a `[ClosedHierarchy]` base is an open generic (`record Wrapped<T> : OrderError`). | Its cases can't be listed, so `Match` and `[Union]` can't be complete. |
| `RFG0004` | error | A `public` shape exposes a leaf that's less accessible than the shape. | The union constructor and `TryGetValue(out Leaf)` would be CS0051. |
| `RFG0005` | error | A declaration that carries `[ClosedHierarchy]` or `[ResultShape]` is nested in a type that isn't `partial`. | The generator can't reopen the enclosing type. |
| `RFG0006` | error | `[ResultShape]` is on something other than a `readonly partial struct`, or `[ClosedHierarchy]` on something other than an `abstract partial` record or class. | The generator can't add members. |

**Verified** on SDK 11.0.100 with a probe (`temp/gendesign-probe`):

- A `public` constructor that takes an `internal` leaf is CS0051 (`RFG0004`).
- `Mid => …, Leaf => …`, where `Leaf : Mid`, is CS8510. With `Leaf` first, it compiles. This is why ordering matters
  for `RFG0002`.
- A user-defined conversion from an interface is CS0552.

### Edge cases the generator handles without a diagnostic

- **Deep hierarchies.** Cases are found transitively through intermediate bases, at any depth. Every `switch`,
  `Match`, `Switch` and `TryMap` the generator writes orders cases most-derived first: a topological sort over the
  base relation, with ties broken by fully qualified name so the output is deterministic. CS8510 can't fire.
- **Leaves in another assembly.** When a shape's error type comes from a referenced assembly, the generator lists
  its cases by walking `errorType.ContainingAssembly.GlobalNamespace`. It doesn't use the current compilation's
  syntax.
- **Leaves with the same short name.** `Orders.NotFound` and `Payments.NotFound` under one base would both become
  `onNotFound`. When short names collide, the generator prefixes each colliding name with as many enclosing type
  and namespace segments as it takes to make it unique: `onOrdersNotFound` and `onPaymentsNotFound`. Names that
  don't collide stay short.
- **Hint names.** Each file is named from the type's namespace, its enclosing types and its metadata name
  (`App.Orders.Outer+PlaceOrderResult.ResultShape.g.cs`). Two types with the same name in different namespaces or
  enclosing types can't share a hint name. A shared hint name throws inside the generator and surfaces as CS8785,
  which drops all of its output.
- **`Success` is an interface.** C# forbids a conversion from an interface (CS0552, verified), so the generator
  leaves out `implicit operator Shape(TSuccess)` and the caller writes `Shape.Ok(value)`. Every other member is
  emitted.
- **Nested declarations.** The generator reopens each enclosing type as `partial`, keeping its kind (class,
  struct, record or record struct) and accessibility.

## The incremental pipeline

These rules come from `Parquet.SourceGenerator`. Its `Models/DiagnosticInfo.cs` and incrementality tests show
each one.

1. **Two stages.**
   - **Stage A** is a `CreateSyntaxProvider` whose predicate keeps only class and record declarations with a
     `BaseList`. Its transform records each type's fully qualified name, its direct base, its interfaces, and
     whether it's abstract, sealed, generic or less accessible. The result is a collected index of declared bases.
   - **Stage B** uses `ForAttributeWithMetadataName` for `[ClosedHierarchy]` and for `[ResultShape]`, then
     `Combine`s each with the Stage A index to find every case.

   An edit to a type with no base list never reaches Stage B.
2. **Only plain data is cached.** A model never holds an `ISymbol`, a `SyntaxNode`, a `Location` or a `Compilation`.
   - Positions are kept as a `SourcePosition` (file path, span, line span).
   - Diagnostics travel through the pipeline as a `DiagnosticInfo`, and the `Location` is built again when the
     diagnostic is reported.
   - A Roslyn `Location` holds its syntax tree, so caching one keeps old trees alive for the life of the driver.
3. **Collections compare by value.** Models use `EquatableArray<T>`, never `ImmutableArray<T>` or `List<T>`, so
   an equal model is reported `Unchanged` and the step after it is skipped.
4. **Reference checks are a single `bool`.** Whether .NET 11's `UnionAttribute` exists is a
   `CompilationProvider.Select` that returns one `bool`. Nothing after it reruns until that value changes.
5. **One output per type.** A type declared in several `partial` parts produces one model and one file.
6. **Every step has a tracking name**, so tests can assert on it:

   | Step | Tracking name |
   |---|---|
   | Stage A index | `Resultful.BaseIndex` |
   | `[ClosedHierarchy]` models | `Resultful.Hierarchies` |
   | `[ResultShape]` models | `Resultful.Shapes` |
   | Union available | `Resultful.UnionAvailable` |
   | Final emit models | `Resultful.Emit` |

7. **Error types from referenced assemblies.** Walking a referenced assembly needs the compilation, which changes
   on every edit. That walk runs only for shapes whose error type comes from metadata. It returns an
   `EquatableArray` of cases, so the steps after it stay cached when nothing changed.

## Assembly size

`OneOf` writes a large class for every union. Resultful keeps generated code to what depends on the user's types
([decision 16](../decisions/0016-source-generated-result-shapes.md) item 8):

- **One field.** A shape holds one `private readonly` core value, `_inner`: a `Result<T, TError>`, a
  `Result<Unit, TError>` or a `Validation<T, TError>`. Its size is the core value's size.
- **What a shape emits:**
  - `Ok`, `Fail` and `AsResult()`
  - implicit conversions from `Success` and from each leaf, and to the core types
  - `TryGetValue` and `TryGetError`
  - `Match`
  - on .NET 11, the `[Union]` members: `HasValue`, `Value`, and a constructor and a `TryGetValue(out Leaf)` for
    each leaf
  - every member is a one-line forward to `_inner`, marked `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- **What it doesn't emit:** `Bind`, `Tee`, async variants or satellite helpers. They live once, in the core or a
  satellite.
- **A shape implements a core interface:** `IResultShape<T, TError>`, or `IValidationShape<T, TError>` for a shape
  that gathers. Satellites (`Resultful.AspNetCore`, `Resultful.Text.Json`) are written against that interface.

**Verified** on SDK 11.0.100 (`temp/gendesign-probe`):

- An extension method declared as `this IResultShape<T, TError> s` binds to a shape struct, and `T` and `TError`
  are inferred.
- Declared as `this TShape s where TShape : struct, IResultShape<T, TError>`, it fails with CS0411, because C#
  doesn't infer from constraints.

So the interface form works, but it boxes the struct on every call. That's fine for a satellite at an HTTP or
JSON boundary. On the hot railway path it's an allocation per step. The experiment measures it with a
`[MemoryDiagnoser]` benchmark, which also shows whether .NET 11 removes the box. Until that's measured, the
generator also forwards `Map` and `Bind`, two lines each, and they count against the budget below.

## Checking the emitted `.g.cs`, with no golden files

`Parquet.SourceGenerator` keeps golden output files. Resultful doesn't: a golden file fails on every harmless
formatting change and passes a bug that's already in the file. Instead, the tests check properties of the output.

### 1. The emitted files compile on their own

The tests run the generator over fixture projects, write the generated files out, and compile them as ordinary
source with:

- `generated_code = false`, so analyzers treat the files as user code
- `ImplicitUsings` disabled
- `Nullable` enabled
- `IsAotCompatible = true`
- `TreatWarningsAsErrors = true`
- no baseline of suppressed warnings

The fixtures cover:

- a flat hierarchy
- a hierarchy four levels deep with intermediate bases
- a concrete intermediate
- leaves that share short names
- nested shapes
- an interface `Success`
- a valueless shape
- a shape that gathers
- a cross-assembly hierarchy

### 2. Rules checked on the syntax and symbols

| Rule | Checked by |
|---|---|
| No `using` directives in generated files | syntax |
| Every type name is `global::`-qualified | syntax: no unqualified `QualifiedNameSyntax` outside `global::` |
| Each file starts with `// <auto-generated/>` and `#nullable enable` | text |
| Each shape has exactly one instance field, `_inner`, of a core type | symbols |
| Each shape carries `[MustObserve]` | symbols |
| Every forwarding member has `AggressiveInlining` | symbols |
| Cases in each generated `switch` and `Match` run most-derived first | syntax: no arm's type derives from an earlier arm's |
| On .NET 11, the `[Union]` members cover every leaf exactly once | symbols: the constructor and `TryGetValue` sets equal the leaf set |
| On `net10.0`, no `[Union]` members are emitted | symbols |

### 3. A budget for API and IL

The budgets live in `src/Resultful.Generators/api/emitted-api-budgets.txt`, as in `Parquet.SourceGenerator`.
`L` is the number of leaves in the shape. These are starting ceilings. The experiment measures each value and
lowers the ceiling to the measured value plus 25%, recording the numbers in the same file.

| Shape | `MEMBERS` (public) | `PARAMETERS` (most on one member) |
|---|---|---|
| narrow, `net10.0` | `12 + L` | `L + 1` |
| narrow, `net11.0` | `14 + 3L` | `L + 1` |
| valueless | the same as narrow | the same as narrow |
| gathering (`ManyErrors`) | `14` | `3` |
| `[ClosedHierarchy]` base | `3` (`Match`, `Switch`, `TryMap`) | `L + 2` |

Compiled IL, read from the fixture assembly with `System.Reflection.Metadata`:

- every forwarding member: at most 32 bytes
- `Match`, `Switch` and `TryMap`: at most `32 + 24L` bytes
- no `box` instructions in any member except `Value`, whose return type is `object`

The gate fails if it finds no shapes, so a broken fixture can't pass with nothing checked.

### 4. Incremental steps

These tests use `GeneratorDriver` with `trackIncrementalGeneratorSteps: true`, as in
`Parquet.SourceGenerator`'s `IncrementalityTests`:

- **An unrelated edit** (a comment in a file with no attributes or base lists): every tracked step is `Cached` or
  `Unchanged`, and no source is emitted again.
- **An edit to a method body in a leaf's file:** `Resultful.Emit` is `Unchanged`.
- **A new leaf in another file:** `Resultful.BaseIndex` and `Resultful.Emit` for that hierarchy are `Modified`.
  Shapes over other hierarchies stay `Cached` or `Unchanged`.
- **Renaming a leaf:** the shape's output changes, and only that shape's.
- **No cached model holds a Roslyn object:** a reflection walk over the model types finds no `ISymbol`,
  `SyntaxNode`, `Location` or `Compilation` field.

## What the experiment settles

- Whether the interface box disappears on .NET 11, which decides whether `Map` and `Bind` stay forwarded.
- The measured member and IL numbers, which replace the starting ceilings.
- Whether `generated_code = false` really makes analyzers run on files that carry `// <auto-generated/>`. If it
  doesn't, the bare compile removes the header first.
- How long the Stage A index takes on a large solution, and whether the walk over referenced assemblies needs
  caching keyed on the assembly's identity.
