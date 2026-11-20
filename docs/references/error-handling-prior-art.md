---
type: reference
title: "Error handling: prior art"
description: "How other .NET libraries and other languages shape an error and compose errors, what Resultful takes from each, and what it leaves and why."
status: draft
tags: [resultful, reference, errors]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Error handling: prior art

> Reference material for the [one error or many exploration](../explorations/one-error-or-many.md) and
> [decision 14](../decisions/0014-result-and-validation.md). Each entry was read from the source at the commit linked,
> except where it says *from documentation*.

## The questions

Every design below answers the same five questions. Comparing them this way is what shaped Resultful's answer.

1. **What is an error?** A string, a fixed record, a class you derive from, or any type?
2. **How are categories expressed?** An enum, a code, or the error's own type?
3. **How do independent errors combine?** A list in the result, or an error that holds errors?
4. **How does an error gain context as it passes up through layers?**
5. **How does a handler find out what went wrong?** Matching on the top value, or searching through the
   structure?

## .NET libraries

### ErrorOr

[`Error.cs`](https://github.com/amantinband/error-or/blob/d7cdea2ddc76/src/Errors/Error.cs),
[`ErrorType.cs`](https://github.com/amantinband/error-or/blob/d7cdea2ddc76/src/Errors/ErrorType.cs)

- A `readonly record struct Error` with `Code`, `Description`, `Type` and a `Dictionary<string, object>`
  of metadata.
- Categories are a fixed enum: `Failure`, `Unexpected`, `Validation`, `Conflict`, `NotFound`,
  `Unauthorized`, `Forbidden`, plus `Custom` with a number.
- Errors combine as a `List<Error>` inside `ErrorOr<T>`; an error never contains another.
- No context or cause.

**Taken:** keeping several errors in a list rather than inside an error, which `Validation` does.
**Left:** the enum. Its categories are HTTP status codes in other words, which ties a core type to one boundary,
and a domain with a category the enum lacks falls back to `Custom` and a number. Metadata as a string
dictionary loses types.

### FluentResults

[`Error.cs`](https://github.com/altmann/FluentResults/blob/36284d3633ab/src/FluentResults/Reasons/Error.cs),
[`IReason.cs`](https://github.com/altmann/FluentResults/blob/36284d3633ab/src/FluentResults/Reasons/IReason.cs)

- A class `Error : IError` you derive from, with `Message`, `Metadata` and `Reasons` (a list of causes).
- `CausedBy(...)` adds a cause: another error, an exception or a message. `ExceptionalError` wraps an exception.
- The result holds a list of reasons, including successes.

**Taken:** deriving from `Error` to make your own categories, and the idea of a cause.
**Left:** a list of causes on every error (one cause is enough; several is what combining is for), success
reasons, and string-keyed metadata.

### LanguageExt

[`Error.cs`](https://github.com/louthy/language-ext/blob/54d27a2d76cf/LanguageExt.Core/Common/Error.cs)

- An `abstract record Error` that is a monoid: `operator +` and `Combine` flatten into `ManyErrors`, and
  `Error.Empty` is the identity.
- `Expected` and `Exceptional` subtypes separate failures a caller should handle from exceptions.
- `Inner` is a single cause. `Is`, `IsType<E>()` and `Filter<E>()` look through `ManyErrors`.
- An integer `Code` on every error.

**Taken:** an abstract record, a single cause, and searching by type.
**Left:** combining inside the error (`ManyErrors`); in Resultful the count belongs to the result, so plurality is
`Validation`'s job. `Error.Empty`: an empty error is a failure with nothing wrong, the hazard
[decision 10](../decisions/0010-default-is-not-success.md) removes from `default`. Also left: the integer code (the type is the code) and `Exceptional` (exceptions on bugs stay exceptions, per
the [vision](../vision.md)).

### CSharpFunctionalExtensions

[`ICombine.cs`](https://github.com/vkhorikov/CSharpFunctionalExtensions/blob/e0fc908ecf0b/CSharpFunctionalExtensions/ICombine.cs)

- Errors are strings by default, or any `E`. `ICombine.Combine` lets a custom error type combine, which is the
  same role as Resultful's `IPlus`.

**Compared:** this is the role `IPlus` plays today. With `Validation` holding a list, the error type no longer
has to know how to combine.

### Ardalis.Result

[`ResultStatus.cs`](https://github.com/ardalis/Result/blob/14cf45a126da/src/Ardalis.Result/ResultStatus.cs)

- The result carries a status enum (`Ok`, `Created`, `NotFound`, `Conflict`, `Unavailable`, ...) and a list of
  messages or `ValidationError`s.

**Left:** status on the result. It is an HTTP response model, which belongs in `Resultful.AspNetCore` if
anywhere.

### dotNext

[`Result.cs`](https://github.com/dotnet/dotNext/blob/46a0fc8c2f90/src/DotNext/Result.cs)

- `Result<T>` holds an `Exception`; `Result<T, TError>` holds an enum error.

**Left:** exceptions as the error value. They are expensive to create and carry a stack trace that a failure
the caller expects doesn't need.

### OneOf

[`Assorted.cs`](https://github.com/mcintyre321/OneOf/blob/6e02dbe75d0f/OneOf/Types/Assorted.cs)

- Marker structs in `OneOf.Types`: `Error`, `Error<T>`, `NotFound`, `None`, `Success`, `Unknown`.

**Relevant for the name:** `OneOf.Types.Error` is the collision most likely to affect Resultful users (see
[the exploration](../explorations/one-error-or-many.md#the-name-error)).

## Other languages

### Go: wrapping and searching

[`wrap.go`](https://github.com/golang/go/blob/d4691eecba5a/src/errors/wrap.go),
[`join.go`](https://github.com/golang/go/blob/d4691eecba5a/src/errors/join.go)

- `error` is an interface with one method. `fmt.Errorf("loading user: %w", err)` wraps, `Unwrap()` returns the
  cause, and `errors.Join` combines (`Unwrap() []error`).
- `errors.Is` and `errors.As` search the whole tree, through wraps and joins.

**Taken:** the most important lesson. Once errors are wrapped, a handler cannot just look at the top value; it
searches. Our `TryFind` is `errors.As` over the cause chain.
**Left:** `Join` inside the error value; `Validation` holds the many.

### Rust: `std::error::Error`, anyhow, thiserror, error-stack

[anyhow `error.rs`](https://github.com/dtolnay/anyhow/blob/dd0c0a655a21/src/error.rs),
[thiserror README](https://github.com/dtolnay/thiserror/blob/ba2784aadbe2/README.md),
[error-stack `report.rs`](https://github.com/hashintel/hash/blob/c3a91f7e937b/libs/error-stack/src/report.rs)

- The standard trait gives every error a `source()`, its cause.
- **thiserror**: you define error enums with messages and `#[from]` conversions. Categories are your types.
- **anyhow**: one opaque error for applications. `.context("...")` wraps, `chain()` walks the causes,
  `downcast_ref::<E>()` finds a specific type.
- **error-stack**: a `Report` with `change_context` (move to a higher-level error, keeping the lower one),
  attachments, `contains::<T>()` and `downcast_ref::<T>()`, and `push` to collect several reports.

**Taken:** `WithContext` (anyhow's `context`), searching by type (`downcast_ref`, `contains`), and the split
between defining your own error types (thiserror) and passing them through generic plumbing (anyhow).
**Left:** attachments. A derived record with typed properties does the same job.

### Scala ZIO and Effect (TypeScript): causes

[ZIO `Cause.scala`](https://github.com/zio/zio/blob/c1fd6320af70/core/shared/src/main/scala/zio/Cause.scala),
[Effect `Cause.ts`](https://github.com/Effect-TS/effect/blob/66ab9920271e/packages/effect/src/Cause.ts)

- ZIO's `Cause` is a tree: `Fail` (an expected error), `Die` (a defect), `Interrupt`, and two ways to join
  causes, `Then` (one after another) and `Both` (at the same time).
- Effect's current `Cause` is flatter: a list of reasons (`Fail`, `Die`, `Interrupt`), and `combine` joins two
  lists.

**Taken:** expected failures and defects are different things, and only expected failures are errors. Effect
moving from a tree to a flat list supports keeping `Validation`'s errors a flat list.
**Left:** `Then`/`Both`. Resultful's composition is synchronous; sequence is already expressed by `Bind`
stopping at the first failure.

### F#: `Result` and validation

[FsToolkit `Validation.fs`](https://github.com/demystifyfp/FsToolkit.ErrorHandling/blob/f6c7023e4d7f/src/FsToolkit.ErrorHandling/Validation.fs)

- `Result<'T, 'E>` stops at the first error. `Validation<'T, 'E>` is `Result<'T, 'E list>` and gathers
  errors through `apply` and `map2`.
- Errors are usually discriminated unions you define.

**Taken:** the central idea. `Result` holds one error and stops at the first; `Validation` holds a list and
gathers them all, and the count is in the type. FsToolkit's `Validation` also has a `bind` that stops at the first
failure; whether Resultful's has one is open. F# is also Resultful's original inspiration
([F# for Fun and Profit](https://fsharpforfunandprofit.com/posts/recipe-part2/)). See
[the exploration](../explorations/one-error-or-many.md#the-idea).

### From documentation (not cloned)

- **Swift.** `Error` is a protocol any type adopts, and Swift 6 has typed throws (`throws(MyError)`). Your own
  types are the categories.
- **Kotlin Arrow.** `Raise<E>` with `zipOrAccumulate` gathers errors into a `NonEmptyList<E>`, never an empty one.
- **Haskell.** `Validation e a` accumulates with any `Semigroup e`: a combine operation with no identity
  required.
- **Zig.** Error sets are merged with `||` at compile time.

**Taken:** the working name `Validation` (Haskell; `Validated` in Cats), Haskell's choice to give it no monad
(no `Bind`), and non-empty accumulation (Arrow, Haskell): a
failed `Validation` always has at least one error.

## What the survey says

- **Categories are types.** Rust, Swift, F#, Effect, FluentResults and LanguageExt all let your own types be the
  categories. Only the HTTP-shaped .NET libraries use an enum.
- **One error or many is a choice of shape.** F#, Haskell and Cats put it in the type (`Result` and
  `Validation`); Go and LanguageExt put it in the error value. Resultful follows the first.
- **Many errors are flat and non-empty.** Go's `Join`, Effect's reason list, Arrow's `NonEmptyList`.
- **Context wraps and keeps the cause.** Go, Rust, FluentResults, LanguageExt.
- **Handlers search.** Go's `errors.As`, anyhow's `downcast_ref`, error-stack's `contains`, LanguageExt's
  `Filter`. Once errors are wrapped, matching the top value isn't enough.
- **Defects aren't errors.** ZIO, Effect and LanguageExt separate them; Resultful leaves them as exceptions.
