---
type: vision
title: "Resultful: Vision"
description: "What Resultful is for, what it is and is not, who it is for, its principles and what success looks like."
status: draft
tags: [resultful, vision]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Resultful: Vision

> Draft. The specifics are in [the roadmap](roadmap.md) and [the decisions](decisions/index.md).

## Intention

Failure is part of what a function does, so it belongs in the function's signature. Resultful exists so that C#
code can say "this can fail, and here is how" in its types, and so that composing those functions stays as easy
to read as the happy path.

In idiomatic C#, failure is usually hidden: exceptions, `null`, `TryX(out ...)` or status objects the compiler
knows nothing about. Callers forget to handle it, errors from independent checks cannot be gathered together,
and a sequence of fallible steps becomes nested `if`s and `try` blocks. Railway Oriented Programming solves this
in functional languages. Resultful brings it to C# in C#'s own idiom, so it doesn't feel like a foreign language
inside your codebase:

```csharp
var status = orderResult switch
{
    int v => v,
    OrderNotFound nf => nf.Id == 42 ? 404 : 500,
    OrderLocked => 409,
    CardDeclined => 402,
};
```

No base class to inherit, no `_` catch-all, and no boxing — add a new failure case and the compiler tells every
caller that hasn't handled it.

## What Resultful is

- **A small set of result types.** Success and failure are values: a result with a typed error, a result with
  message errors, results with no success value, and an optional value.
- **A railway in C#'s own idiom.** Map, bind, tee and match, implicit conversions from either case, LINQ query
  syntax, and async forms that read like the sync ones.
- **Both ways of handling failure.** Independent checks gather every error. Dependent steps stop at the first
  failure. The caller chooses which by the operator they use.
- **A foundation that costs nothing to take.** The core has no dependencies, allocates as little as the language
  allows, and works under trimming and native AOT. Interop with other libraries ships separately.
- **In step with the language.** As C# gains native features for this (discriminated unions, exhaustive
  matching), Resultful adopts them rather than competing with them. Domain result shapes generated from closed
  error hierarchies lower directly to `.NET 11` `[Union]` structs, so callers `switch` exhaustively over success
  and every error case with no `_` arm and no base-class inheritance.

## What Resultful is not

- **Not a general functional-programming library.** It has no type-class hierarchy, monad transformers or
  persistent collections. Libraries such as [LanguageExt](https://github.com/louthy/language-ext) cover that ground.
- **Not a union library.** It uses whatever union mechanism serves its types best, and doesn't offer
  general-purpose unions of its own.
- **Not a replacement for exceptions on bugs.** Programming errors still throw. Results are for failures a caller
  is expected to handle.
- **Not a validation framework.** It provides the plumbing to collect errors, not rules, messages or localisation.

## Who it is for

C# developers who want failure in their signatures without leaving C#'s idioms. That covers application and
service code at boundaries such as controllers, handlers and pipelines, and libraries that cannot afford a heavy
dependency. It supports the oldest target that libraries still need as well as the newest runtime, so the same
code works across both.

## Principles

- **Explicit over hidden.** If something can fail, its type says so. If something can't fail, its type doesn't
  pretend it might.
- **Idiomatic over pure.** Names, overloads and async follow C# conventions, even when a functional language would
  spell it differently.
- **Nothing to inherit.** The core package depends on nothing. A dependency graph is part of the API.
- **Additive by default, breaking on purpose.** New operators extend the API. A break is a recorded decision and
  ships with a migration path.
- **Measured, not assumed.** Performance and design choices carry evidence, and correctness is settled before
  speed is measured.
- **Laws, not just examples.** The types obey the laws their operators imply, and tests check those laws.
- **Checked, not remembered.** Package shape, style, analyzers and coverage are enforced by CI, not by review.

## Success

- Adopting Resultful adds one package and nothing else to a project's dependency graph.
- Code written with it reads top to bottom as the happy path, with every failure accounted for in the types.
- On each supported target the API and behaviour are the same, apart from language features that are only
  available on newer targets.
- When C# offers native unions, a Resultful type works with them naturally and is no slower for it.
- A release is cut from CI alone, and no consumer has to work around one.
