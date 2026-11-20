---
type: exploration
title: "Analyzers for your own types"
description: "Driving every Resultful analyzer rule from a small set of attributes, so a user's own result and error types get the same checks as Resultful's, including exhaustive switches over a closed error hierarchy."
status: draft
tags: [resultful, exploration, analyzers]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Analyzers for your own types

> Draft exploration behind [decision 15](../decisions/0015-analyzer-rules-read-attributes.md), which is
> **proposed**. Claims marked **verified** were compiled on SDK 11.0.100, with the analyzer built for
> `netstandard2.0` against `Microsoft.CodeAnalysis.CSharp` 4.14.0 and the consumer on `net11.0`. Everything else is
> design.

## The idea

The analyzers ([roadmap M4](../roadmap.md), a separate package per
[decision 11](../decisions/0011-package-family.md)) shouldn't know Resultful's types by name. Each rule reads an
attribute, and Resultful's own types are the first to carry it. A user puts the same attribute on their own type
and gets the same checks.

- If a rule works for `Result<T>`, it works for a user's `ApiResult<T>`. If it doesn't, the attribute is missing
  something, and the rule's tests show it.
- Satellites add their own attributes the same way. `Resultful.AspNetCore` could read a status on a user's error
  type without the core knowing anything about HTTP.

## The contract

- **The attributes live in the core**, in `Resultful.CodeAnalysis`. They are plain attributes with no
  dependencies and work on `netstandard2.0`.
- **The analyzers match them by name, not by assembly.** .NET's nullable attributes work the same way, which is
  why polyfilled copies work. A library that doesn't want to reference Resultful declares its own `internal` copy.
  **Verified**: the consumer's own `internal` `ClosedHierarchyAttribute` drove the probe below.
- **Types you can't put attributes on** (a third-party package's) are listed in `.editorconfig`, as the built-in
  .NET analyzers do with their options. For example:
  `resultful.must_observe_types = T:Vendor.ApiResponse`.
- **The attribute names and meanings are public API**, tracked in the `PublicAPI` files and versioned like
  everything else.

## The attributes

Start with two. They cover most of the M4 rules.

| Attribute | On | Rule |
|---|---|---|
| `[MustObserve]` | a type or a method | A value of this type, or returned by this method, that is thrown away. Like JetBrains' `[MustUseReturnValue]`. |
| `[ClosedHierarchy]` | an abstract class or record | A `switch` over it is exhaustive when every derived type is matched; a type outside its assembly may not derive from it. |

Candidates for later, each added only when a rule needs it:

| Attribute | On | Rule |
|---|---|---|
| `[ResultShape]` | a `partial struct`, naming its error type and success type | Defined by [decision 16](../decisions/0016-source-generated-result-shapes.md) for the generator; the analyzers can read it too (`default(T)`, a success type that overlaps an error case) |

An earlier `[ContextWrapper]` candidate (warn on a `switch` over an error that may be wrapped) is dropped:
`WithContext` now adds a note and keeps the type ([decision 14](../decisions/0014-result-and-validation.md)), so
a `switch` still sees the real error.

Resultful's own `Result`, `Validation` and `Option` would carry `[MustObserve]`. `Error` stays open
([decision 14](../decisions/0014-result-and-validation.md)), and a user closes their own base:

```csharp
[ClosedHierarchy]
public abstract record OrderError : Error
{
    private protected OrderError(string message) : base(message) { }   // or `partial`, and the generator writes it
}
public sealed record NotFound(int Id) : OrderError($"order {Id} not found");
public sealed record Conflict(string Reason) : OrderError(Reason);

int Status(OrderError e) => e switch { NotFound => 404, Conflict => 409 };   // no `_` arm needed
```

## Exhaustive switches: the probe

C# warns CS8509 on a `switch` expression over an abstract type without a `_` arm, because the compiler can't know
the set of derived types. A `DiagnosticSuppressor` can know it from the attribute, and suppress the warning only
when every case is matched. **Verified**:

| Switch | Result |
|---|---|
| `[ClosedHierarchy]` base, every derived type matched (`NotFound => …, Conflict => …`) | CS8509 suppressed |
| the same, with declaration patterns (`NotFound n => …`) | CS8509 suppressed |
| one derived type missing | CS8509 kept |
| every type matched, but one arm has a `when` guard | CS8509 kept |
| a base without the attribute | CS8509 kept |
| every type matched, built with `TreatWarningsAsErrors` | builds |
| a nullable input (`OrderError?`), every type matched, no `null` arm | CS8509 suppressed, **a gap**: `null` throws at run time. The rule keeps the warning unless there's a `null` arm |

If a value of a new type arrives anyway (from code that ignored the rule), the `switch` throws
`SwitchExpressionException` at run time, as any non-exhaustive switch does.

What the probe did not cover:
- **Across assemblies.** The probe searched every type the compilation could see. The real rule looks only in the
  base's own assembly, and a second rule reports any type in another assembly that derives from the base. Code
  that doesn't run the analyzer can still derive, so the guarantee is only as strong as the analyzer's reach.
- **Cost.** Finding derived types should be computed once per compilation, not once per `switch`.
- **IDEs.** Suppressors work in the build; Visual Studio, Rider and VS Code weren't checked.
- **`switch` statements and nested patterns.** Only `switch` expressions with top-level type patterns were
  probed.

## Closing the base

The attribute says the set is closed; the constructor makes it so. A positional `abstract record
OrderError(string Message) : Error(Message)` gets a `protected` constructor, and another assembly can call it
(**verified**). A rule reports a closed base with a `public` or `protected` constructor and suggests
`private protected`. `Resultful.Generators` writes that constructor when the base is `partial`
([decision 16](../decisions/0016-source-generated-result-shapes.md)); the analyzers don't need the generator.

## With generated shapes

On .NET 11, a generated `[ResultShape]` struct is a native union, so the compiler checks its `switch` with no
suppressor (**verified** on the exploration's probe). The suppressor is still what checks a `switch` over the base
itself, over each error in a gathering shape's list (the compiler reports CS8509 there, **verified**), and every
`switch` on `net10.0` and `netstandard2.0`.

## Prior art

- **.NET nullable attributes** (`NotNullWhen`, `MemberNotNullWhen`): the compiler matches them by name, so a
  project can declare its own. The model for matching by name.
- **JetBrains.Annotations** `[MustUseReturnValue]`, `[MustDisposeResource]`: attributes as a contract between
  library and analyzer (from documentation).
- **ExhaustiveMatching.Analyzer** `[Closed(typeof(…))]`: exhaustive `switch` checks over a closed hierarchy, which
  reports missing cases rather than suppressing CS8509 (from documentation).
- **.NET code-quality analyzers**: rule options in `.editorconfig`, such as lists of types to include or exclude.
- **C# unions** ([decision 3](../decisions/0003-unions-are-chosen-by-measurement.md)): if the language gets
  closed hierarchies or unions that are exhaustive by construction, `[ClosedHierarchy]` gives way to them. The
  attribute is a bridge.

## Costs

- Each attribute is a permanent part of the core's public API. Start with the two that pay for themselves.
- A rule driven by attributes is harder to write than one that hard-codes `Resultful.Result<T>`. Every rule is
  tested against a user's own type as well as Resultful's.
- Matching by name means anyone can declare the attribute, including by mistake. The analyzer checks the full
  name (`Resultful.CodeAnalysis.ClosedHierarchyAttribute`), not just the short name the probe used.

## If decision 15 is accepted

- The core gains `Resultful.CodeAnalysis` with `MustObserveAttribute`, `ClosedHierarchyAttribute` and
  `ResultShapeAttribute` in the breaking release (M3), so `Result`, `Validation` and `Option` carry them from the start.
- Roadmap M4's rules read attributes. "A result that is never observed" becomes `[MustObserve]`; exhaustive
  `switch` over a closed hierarchy is added.
- Satellites may define their own attributes for their own rules.
