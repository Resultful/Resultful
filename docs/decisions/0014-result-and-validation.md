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
   - A `Result` converts to a `Validation` implicitly. A `Validation` becomes a `Result` only through
     `ToResult(combine)`, where the caller says how many errors become one.
2. **`Error` describes one failure.** An `abstract record` with `Message`, an optional `Cause`, an implicit
   conversion from `string` (to `MessageError`), `WithContext`, `Causes` and `TryFind<T>`. Categories are records
   the user derives. There is no `+`, aggregate, code, category enum or metadata dictionary.
3. **`Result<T>` is `Result<T, Error>`**, replacing `IEnumerable<string>`, and **`Validation<T>` is
   `Validation<T, Error>`**.
4. **The name is `Error`.** It clashes with `OneOf.Types.Error`, `ErrorOr.Error` and `FluentResults.Error`
   (CS0104); the README shows `global using Error = Resultful.Error;`, which resolves it. Nothing in .NET 11 or
   ASP.NET Core 11 has the name.

Still open (see the exploration): the plural type's name, whether it is a new type or today's
`Result<T, IEnumerable<TError>>` renamed, `Bind` on it, and how many errors become one inside a `Result` chain.
`Validation` is checked for clashes: none in .NET 11 or ASP.NET Core 11; LanguageExt has `Validation<F, A>`.

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
- `Result<string> r = "message"` changes from failure to success silently. The analyzer rule for a bare string
  (roadmap M4) ships in the same release.
- **Changes [decision 9](0009-operators-are-opt-in-sugar.md):** the accumulating operators (`+ - *`, `&`) belong
  to `Validation`, not `Result`. `IPlus` and `Result<T, TError>.Plus` are proposed for deprecation; when is open.
- Roadmap M5's additive structured `Error` is replaced. `Resultful.AspNetCore` maps `Validation<T>` to a
  validation problem and `Result<T>` by error type; `Resultful.FluentValidation` returns `Validation<T>`.
