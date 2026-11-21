---
type: decision
title: "One error or many: Result, Validation and Error"
description: "Result holds one error and stops at the first; Validation holds a non-empty list and gathers them all; a small abstract Error record is the default error for both."
status: proposed
tags: [resultful, decision, errors]
decided:
  by:
  at:
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 14. One error or many: `Result`, `Validation` and `Error`

**Proposed.** The reasoning, the scenario written three ways and the migration are in
[One error or many](../explorations/one-error-or-many.md). The prior art is in
[Error handling: prior art](../references/error-handling-prior-art.md).

1. **How many errors is part of the type**, as in F# (FsToolkit), Haskell and Cats:
   - `Result<T, TError>` holds one error. `Bind` stops at the first failure.
   - `Validation<T, TError>` holds a non-empty `IReadOnlyList<TError>`. `&` keeps every error from both sides,
     in order. It has no `Bind` (as in Haskell and Cats; FsToolkit has one), which is still open.
   - A `Result` converts to a `Validation` implicitly. A `Validation` becomes a `Result` through `ToResult()`
     when its errors derive from `Error` (they travel as one `ValidationFailed<TError>`), or through
     `ToResult(combine)`, where the caller says how many errors become one.
   - `Validation.Combine(a, b, c, (x, y, z) => …)` combines more than two without nested tuples.
   - A step with no value is `Result<Unit, TError>` or `Validation<Unit, TError>`, using the `Unit` that ships
     today. `return Unit.Value;` converts implicitly. There is no separate void type: `VoidResult` and
     `VoidResult<TError>` are deprecated, so every method is written once.
2. **`Error` describes one failure.** An `abstract record` with `Message`, an optional `Cause`, a `Context` list,
   `Error.From(string)`, `Inner`, `Causes` and `TryFind<T>`, plus `WithContext`, which adds a note and keeps the
   error's type. `MessageError` and `ValidationFailed<TError>` are built in. Categories are records the user
   derives. There is no implicit conversion from `string`, and no `+`, aggregate, code, category enum or metadata
   dictionary.
3. **`Result<T>` means `Result<T, Error>`**, replacing `IEnumerable<string>`, and **`Validation<T>` means
   `Validation<T, Error>`**. They are separate types in C#. `AsResult()` widens `Result<T, TError>` to `Result<T>`
   when `TError : Error`; generated shapes ([decision 16](0016-source-generated-result-shapes.md)) widen
   implicitly.
4. **The name is `Error`.** It clashes with `OneOf.Types.Error`, `ErrorOr.Error` and `FluentResults.Error`
   (CS0104); the README shows `global using Error = Resultful.Error;`, which resolves it. Nothing in .NET 11 or
   ASP.NET Core 11 has the name.

`Validation` is checked for clashes: none in .NET 11 or ASP.NET Core 11; LanguageExt has `Validation<F, A>`.
`Unit` clashes with `System.Reactive.Unit`, `MediatR.Unit` and `LanguageExt.Unit` when their namespaces are
imported; the same global alias resolves it.

Amended the same day, by the maintainer's choice: `Result<Unit, TError>` replaces the void types, including the
proposed `VoidValidation`, and a generated shape can gather errors
([decision 16](0016-source-generated-result-shapes.md)), with `Validation` kept in the core for the steps between.

Why: a signature should say whether failures stop or gather. A handler should be able to match on a failure
without comparing strings. The satellites ([decision 11](0011-package-family.md)) need one error type to map
from without a user callback. Every member of `Error` is justified in the exploration, and any member no
satellite, sample or law test uses is removed at the 1.0 review.

Alternatives rejected:
- **Keep strings.** A handler can't tell failures apart, and satellites have nothing to map from.
- **An aggregate inside `Error`** (an earlier draft). The count hides in the value, and a signature can't say
  whether it gathers errors.
- **`Result<T, Errors>` as the plural form.** `Error` and `Errors` are one letter apart, and the plural form
  would still have `Bind`.
- **No built-in error.** Every user writes the same context and mapping, and every satellite takes a callback.
- **A structured `Error` with `Code`, `Type` and `Metadata`** (the earlier roadmap item, and ErrorOr's shape). The
  type enum ties a core type to HTTP; the type of the record already is the category.

Consequences, if accepted:
- A break, shipped with `0.3.0-beta01` ([decision 8](0008-version-0-3-0.md)). It changes 69 lines of the shipped
  API.
- `Result<string> r = "message"` changes from failure to success silently, because today's conversion from a
  string to a failure goes. The analyzer rule for a bare string (roadmap M4) ships in the same release.
- **Changes [decision 9](0009-operators-are-opt-in-sugar.md):** the accumulating operators (`+ - *`, `&`) belong
  to `Validation`, not `Result`. `Validation` and `Unit` arrive in roadmap M2 as new types, so M2 builds the
  operators on them and nothing shipped there breaks in M3. `IPlus` and `Result<T, TError>.Plus` are proposed
  for deprecation; when is open.
- `VoidResult` and `VoidResult<TError>` (169 lines of the shipped API, and a second copy of every extension) are
  deprecated. `Unit` and the conversion from `Result<Unit>` to `VoidResult` already ship; M2 adds the conversion
  back. M3 marks them `[Obsolete]` and makes `Result.Ok()` (which returns `VoidResult` today) return `Result<Unit>`.
  The 1.0 review removes them. [Decision 4](0004-sequential-composition-is-additive.md)'s `Sequence` chains become `Result<Unit>` steps.
- Roadmap M5's additive structured `Error` is replaced. `Resultful.AspNetCore` maps `Validation<T>` and
  `ValidationFailed` to a validation problem and `Result<T>` by error type, walking an error's causes from the
  outside in so the first mapped type wins; `Resultful.FluentValidation` returns `Validation<T>`.

## Still open

The one list of open questions for the error design; other documents link here.

- The plural type's name.
- Whether it is a new type or today's `Result<T, IEnumerable<TError>>` renamed.
- Whether `Unit` stays a class (as shipped) or becomes a struct in M3, and how a parameterless `Ok()` reaches
  `Result<Unit, TError>`, whose `TError` it can't infer.
- When `IPlus` and `Result<T, TError>.Plus` are deprecated.
Decided in review: no implicit conversion from `string`; context is a note that keeps the type; a `Validation`
crosses into a `Result` chain as `ValidationFailed`. `Validation` has no `Bind` (as in Haskell and Cats) and no `+`:
it combines with `Validation.Combine`, and `&` arrives with the opt-in operators (stack PR 18).

Decided when `Error` was built (0.3, stack PR 06):
- `Context` takes part in equality, compared element by element, so two identical errors are equal before and
  after the same `WithContext`. It is an `IReadOnlyList<string>` (no dependency, [decision 1](0001-core-is-dependency-free.md)),
  copied when set.
- The base seals `ToString`. It prints the type name, `Message`, `Context`, inner errors and the cause chain, never a
  derived record's own properties, which may hold personal data. The text doesn't depend on the culture. A record
  deriving from `Error` therefore needs C# 10 or later (C# 9 reports CS8912).
- `Causes()` is depth first: the error, then its `Cause`'s tree, then each `Inner` error's tree. `TryFind` returns
  the first match in that order. Each error object is visited once, so an `Inner` that points back up still ends,
  and `ToString` writes `…` for a cycle or past 64 levels; diagnostics never throw.
- `ValidationFailed`'s `Message` is "Validation failed.", and it rejects an empty list.

Decided when `Result<T>` moved to `Error` (0.3, stack PR 09):
- `Result<T>` converts from a `T` and from any `Error`. `Result.Fail<T>(string)` and `Result.Fail(string)` build a
  `MessageError` explicitly. The conversions from `string`, `string[]`, `List<string>` and
  `Result<T, IEnumerable<string>>` are gone.
- `MapError` on `Result<T>` (and on `Task<Result<T>>`) takes `Func<Error, Error>` and returns `Result<T>`, so
  `MapError(e => new SaveFailed(id) { Cause = e })` stays a `Result<T>`. The generic `MapError<TError>` is not kept:
  with it, that lambda picked the generic overload and returned `Result<T, SaveFailed>`. `Map2` changes the error to
  another type.
- Gathering on `Result<T>` and `VoidResult` with no merge function (`Plus`, `Fold`, `Reduce`, `Unroll`, the tuple
  `Plus` and the async forms) is removed; the `Until` forms, which stop at the first failure, stay. `Result<T, TError>`
  keeps its forms that take a merge function. Independent checks gather in `Validation` (PR 10).
- `VoidResult` holds an `Error` too, so it and `Result<Unit>` convert without loss, and it converts from any `Error`.
  Its generic `MapError<TError>` keeps the overload trap (`v.MapError(e => new SaveFailed(id) { Cause = e })` gives
  `VoidResult<SaveFailed>`); it is documented, not fixed, because `Result<Unit>` replaces `VoidResult` in PR 12. `AsResult()` and `WithContext` on
  results arrive here.

Decided when `Validation` was built (0.3, stack PR 10):
- `Validation<T, TError>` keeps its own state byte, value and read-only list; `Validation<T>` wraps a
  `Validation<T, Error>`. The zero value throws on use as a result's does.
- The way into a `Result<T>` when the errors derive from `Error` is `ToResult()`, as this decision writes it (not
  `AsResult()`, which names a lossless widening of one error). `ToResult(combine)` is the explicit form.
- `Validation<T>.MapError` takes `Func<Error, Error>` and stays a `Validation<T>`, as `Result<T>.MapError` does.
- `VoidResult` converts to `Result<Unit>` and `VoidResult<TError>` to `Result<Unit, TError>`, so both directions
  exist. `Validation` has `==` and `!=`; equality compares the errors element by element, as `ValidationFailed` does.
- `IValidationShape<T, TError>.AsValidation()` is what a gathering shape implements, and the `AsValidation()`
  extension widens `Validation<T, TError>` (where `TError : Error`) to `Validation<T>` with the errors kept flat, as
  `AsResult()` does for one error (decision 16 item 8).
- Source break: with conversions both ways between `VoidResult` and `Result<Unit>` (and the generic pair), a
  conditional `b ? voidResult : resultUnit` no longer has a type and reports CS0172. Cast one side.
