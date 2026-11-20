---
type: decision
title: "Analyzer rules read attributes, so users' own types get the same checks"
description: "Every Resultful analyzer rule is driven by a small set of attributes in the core, matched by name, so a user's own result and error types get the same checks as Resultful's."
status: proposed
tags: [resultful, decision, analyzers]
decided:
  by:
  at:
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 15. Analyzer rules read attributes, so users' own types get the same checks

**Proposed.** The details and the probe are in
[Analyzers for your own types](../explorations/analyzers-for-your-own-types.md).

1. **No rule knows a Resultful type by name.** Each rule reads an attribute, and Resultful's types carry the same
   attributes a user would.
2. **The attributes live in the core**, in `Resultful.CodeAnalysis`, with no dependencies. The analyzers stay in
   `Resultful.Analyzers` ([decision 11](0011-package-family.md)).
3. **They are matched by full name**, so a library can declare its own `internal` copy instead of referencing
   Resultful. Third-party types are listed in `.editorconfig`.
4. **Two attributes to start:**
   - `[MustObserve]`: a value of the type, or returned by the method, may not be thrown away.
   - `[ClosedHierarchy]`: a `switch` over the base is exhaustive when every derived type is matched, and the
     compiler's CS8509 is suppressed. A nullable input (`OrderError?`) also needs a `null` arm; the probe's
     suppressor hid the warning without one, which is a gap the rule closes. A type outside the base's assembly
     that derives from it is reported. That rule is the guard: C# can't close a record hierarchy with
     constructors, because the copy constructor of a non-sealed record must be `public` or `protected` (CS8878), and another assembly derived
     through it (**verified**). A `private protected` base constructor
     still closes the ordinary path, and a rule suggests it.
   Others are added only when a rule or the generator needs them. `[ResultShape(typeof(TError), Success =
   typeof(T), ManyErrors = …)]` is one, defined by [decision 16](0016-source-generated-result-shapes.md).
5. **The generator is a second reader of the same attributes.** `Resultful.Generators` writes the `private
   protected` constructor, `Match` and the shapes, and needs the base declared `partial`. The analyzers don't:
   a hand-written base with its own `private protected` constructor gets every check.

Why: users write their own result and error types, and the checks that make Resultful safe should reach them too.
It also answers the cost in [decision 14](0014-result-and-validation.md) that a `switch` on an open `Error` is
never exhaustive: a user closes their own error base. The CS8509 suppression was verified on SDK 11.0.100.

Alternatives rejected:
- **Rules that hard-code Resultful's types.** Simpler to write; useless for users' own types.
- **Only `.editorconfig` options.** Works for third-party types, but the contract lives away from the type and
  doesn't travel with a library.
- **A separate attributes package.** One more package for a few attributes; matching by name already lets a
  library avoid referencing the core.

Consequences, if accepted:
- The attributes ship in the core with the breaking release (M3), so the result types carry them from the start.
- Roadmap M4's rules are written against the attributes and tested against a user's own type as well as
  Resultful's.
- .NET 11's `[Union]` makes a generated shape's `switch` exhaustive with no suppressor. The suppressor stays for a
  `switch` over a closed base itself (`OrderError e => e switch …`), for each error inside a gathering shape's
  list, and on `net10.0` and `netstandard2.0`. If C# gets closed hierarchies
  ([decision 3](0003-unions-are-chosen-by-measurement.md)), `[ClosedHierarchy]` gives way to them.
