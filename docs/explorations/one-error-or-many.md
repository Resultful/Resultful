---
type: exploration
title: "One error or many"
description: "Splitting failure into two shapes, Result (one error, stops at the first) and Validation (a non-empty list, gathers all), and the small Error type that earns its place between them."
status: draft
tags: [resultful, exploration, errors]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# One error or many

> Draft exploration behind [decision 14](../decisions/0014-result-and-validation.md), which is **proposed**. The
> prior art is in [Error handling: prior art](../references/error-handling-prior-art.md). Claims marked
> **verified** were compiled and run on SDK 11.0.100, with the library built for `netstandard2.0` and the consumer
> on `net11.0`. Everything else is design.

## The idea

FsToolkit, Haskell and Cats keep two shapes apart:

- **`Result`** holds **one** error. `Bind` stops at the first failure, because the next step needs the value the
  failed step didn't produce.
- **`Validation`** holds **one or more** errors. Its checks don't depend on each other, so all of them run and
  every failure is kept. It combines with `Apply` (`&`). Haskell and Cats give it no `Bind` at all; FsToolkit
  gives it a `bind` that stops at the first failure, like `Result`'s.

How many errors there can be is part of the signature, not a property of the error value. Resultful today blurs
the two: `Result<T>` fails with `IEnumerable<string>`, the plural shape under the singular name, and
`Result<T, TError>` accumulates only when `TError` implements `IPlus`.

This proposal names both shapes and keeps them distinct.

```csharp
Result<T, TError>        // a T, or one TError
Validation<T, TError>    // a T, or a non-empty IReadOnlyList<TError>

Result<T>      = Result<T, Error>
Validation<T>  = Validation<T, Error>
```

`Validation` is the established functional name (F#, Haskell, Scalaz; `Validated` in Cats). It fits what the
plural shape is used for: input checks whose failures are expected, can be fixed by the caller and compose. The
name is still open (see the end); `Validation` is used here as the working name.

| Package | Its type | Checked |
|---|---|---|
| .NET 11 and ASP.NET Core 11 | no public `Validation` or `Validated`; the nearest is `System.ComponentModel.DataAnnotations.Validator` | metadata scan, with `Validator` as the positive control |
| LanguageExt | `LanguageExt.Validation<F, A>`, the same arity as `Validation<T, TError>` | from its documentation, not compiled |
| FluentValidation, DataAnnotations | `ValidationResult`, `ValidationFailure`: different names | not a clash |

A project using LanguageExt and Resultful together would need an alias, as with `Error` below.

## The two shapes

### `Result<T, TError>`: one error

Unchanged in behaviour for anyone using their own `TError`. It keeps `Map`, `Bind`, `MapError`, `Then`,
`Sequence`, `Check` and the LINQ query syntax. `Bind` short-circuits.

### `Validation<T, TError>`: one or more errors

Not compiled yet: the prototype covered `Error` only.

```csharp
public readonly struct Validation<T, TError>
{
    public bool IsSuccess { get; }
    public IReadOnlyList<TError> Errors { get; }   // never empty on failure

    public static implicit operator Validation<T, TError>(T value);
    public static implicit operator Validation<T, TError>(TError error);
    public static implicit operator Validation<T, TError>(Result<T, TError> result);  // one error becomes a list of one

    public Validation<TOut, TError> Map<TOut>(Func<T, TOut> map);
    public Result<T, TError> ToResult(Func<IReadOnlyList<TError>, TError> combine);   // many to one, explicit
}

// & combines two validations into a tuple, keeping every error from both sides, in order.
```

It has no `Bind`, following Haskell and Cats rather than FsToolkit. Running a step that needs the value means the
checks are over, so the code moves to `Result` with `ToResult`. That's the line between the two shapes, and the
compiler draws it. This is a choice, listed under "Still open".

Errors are an `IReadOnlyList<TError>`: `IEnumerable<T>` with a count and an index. No .NET interface promises a
non-empty list, so `Validation` only ever builds a failure from at least one error. `default(Validation<…>)` is
invalid at run time like every result type ([decision 10](../decisions/0010-default-is-not-success.md)).

### Crossing between them

| From | To | How | Why |
|---|---|---|---|
| `Result` | `Validation` | implicit | One error is a list of one. Nothing is lost. |
| `Validation` | `Result` | `ToResult(combine)` | Many errors become one only when the caller says how. |

With the list in `Validation`, `IPlus` no longer has to make `TError` combinable. `Result<T, TError>.Plus` and
`IPlus` stay so existing code compiles, and are proposed for deprecation in favour of `Validation`; when is still
open.

## The error type

With plurality in the result, `Error` only has to describe **one** failure. It no longer needs `+`, an aggregate,
or `IPlus`.

```csharp
namespace Resultful;

public abstract record Error
{
    protected Error(string message);

    public string Message { get; }
    public Error? Cause { get; init; }

    public static implicit operator Error(string message);       // a MessageError

    public Error WithContext(string message);                    // a MessageError whose Cause is this
    public IEnumerable<Error> Causes();                          // this, then each Cause in turn
    public bool TryFind<TError>([NotNullWhen(true)] out TError? found) where TError : Error;
}

public sealed record MessageError(string Text) : Error(Text);
```

Categories are records the user derives:

```csharp
public sealed record NotFound(string Entity, int Id) : Error($"{Entity} {Id} was not found");
public sealed record Conflict(string Reason) : Error(Reason);
public sealed record Invalid(string Field, string Reason) : Error($"{Field}: {Reason}");
```

### Does it earn its place?

`Result<T, TError>` and `Validation<T, TError>` already take any error type. A built-in `Error` is justified only
if it:
- replaces the strings in `Result<T>` with something a handler can match on and that can carry data, and
- gives the satellites ([decision 11](../decisions/0011-package-family.md)) one type to target, so
  `Resultful.AspNetCore` maps to a status and `Resultful.FluentValidation` produces errors without a user
  callback, and
- composes on its own: context wraps an error without hiding it, and a handler can find what's underneath,
  with laws that hold for any user's errors, and
- adds nothing a user could do as well with their own type.

If the satellites were dropped, the second point would fall and most of the case with it. That dependency is
stated so it can be checked again.

| Member | Kept? | Why |
|---|---|---|
| `abstract record` | kept | Your own types are the categories, with typed data and value equality. |
| `Message` | kept | Logs, `ToString` and ProblemDetails need text from any error. |
| `Cause` | kept | `WithContext` needs it, and a user's error can wrap a lower one (a `DbError` under `SaveFailed`). |
| implicit from `string` | kept | `Fail("x")` stays as short as today. |
| `WithContext` | kept, under review | One line a user could write. Kept because Go and Rust show it's the most-used operation on errors, and one spelling lets analyzers and satellites recognise it. |
| `Causes`, `TryFind<T>` | kept | Needed once errors are wrapped: matching only the top value misses what's underneath. |
| `+`, `AggregateError`, `IPlus` | **cut** | Plurality is `Validation`'s job. |
| `Has<T>` | **cut** | `TryFind<T>(out _)` does the same. |
| `Code` | **cut** | The type is the code. A user who needs a stable string adds a property. |
| `ErrorType` enum | **cut** | Ties a core type to HTTP. The mapping belongs in `Resultful.AspNetCore`. |
| `Metadata` dictionary | **cut** | A derived record with typed properties does the same and keeps the types. |
| `ExceptionError` | **left out** | Exceptions on bugs stay exceptions ([vision](../vision.md)). Added only if a satellite needs it. |

**Removal test.** At the 1.0 API review (roadmap M7), any member of `Error` that no satellite, sample or law test
uses is removed before it becomes stable.

### Handling an error

A `switch` on the top value works while nothing has wrapped the error. The hierarchy is open, so it always needs a
`_` arm:

```csharp
error switch
{
    NotFound n => Results.NotFound(n.Message),
    Conflict c => Results.Conflict(c.Message),
    _ => Results.Problem(error.Message),
}
```

After `WithContext`, the top value is a `MessageError` and that `switch` falls through to `_` (**verified**). A
handler that may see wrapped errors searches instead, as Go's `errors.As` does:

```csharp
if (error.TryFind<NotFound>(out var notFound)) return Results.NotFound(notFound.Message);
```

## Laws

Each is a property test ([decision 6](../decisions/0006-tests-are-a-mix.md)).

1. **A failed `Validation` is never empty.**
2. **`&` keeps every error, in order.** The errors of `a & b` are `a`'s followed by `b`'s, so grouping doesn't
   change them: `(a & b) & c` and `a & (b & c)` fail with the same list.
3. **`Result` to `Validation` and back loses nothing.** For a failed `r`,
   `((Validation<T, E>)r).ToResult(es => es[0])` equals `r`.
4. **`Result.Bind` stops at the first failure**, and `Validation` has no `Bind`, so it can't.
5. **Context keeps what's underneath.** If `e.TryFind<T>` finds something, `e.WithContext(m).TryFind<T>` finds
   the same thing (**verified** with the earlier prototype, whose cause chain is unchanged here).

## The scenario, three ways

Registering a user. The email and name are checked independently and both failures are reported. Then the email
must not be taken. The endpoint returns 400 for invalid input, 409 for a taken email and 500 otherwise.

**Today.** Accumulating works because `Result<T>` is secretly a list of strings. The endpoint can't tell 400
from 409 without comparing messages.

**With the user's own `TError`.** The user writes `AppError`, makes it combinable with `IPlus`, and writes their
own context wrapper. `Resultful.AspNetCore` and `Resultful.FluentValidation` each need a callback for `AppError`.
The next project writes it all again.

**With `Result`, `Validation` and `Error`** (design, not compiled):

```csharp
Validation<Registration> Validate(string email, string name) =>
    (CheckEmail(email) & CheckName(name))                         // both failures kept
        .Map(t => new Registration(t.Item1, t.Item2));

Result<User> Register(Registration r) =>
    (EmailTaken(r.Email) ? new Conflict("email taken") : CreateUser(r))
        .MapError(e => e.WithContext("registering user"));
```

The endpoint validates first. `Resultful.AspNetCore` turns a failed `Validation<T>` into a validation problem
(400) listing each error. On success it calls `Register` and maps the `Result<User>` error by type: `TryFind`
finds the `Conflict` under the context (409), and anything else is 500. The signatures say which is which, and
the user writes no aggregate and no callback.

The gap: if a single `Result` chain has to carry a validation failure onward, `ToResult(combine)` makes the user
write a record that holds the list, and `TryFind` only walks `Cause`, so it can't see inside that record. That's
the case the earlier `AggregateError` covered. It's listed under "Still open" rather than solved here.

## The name `Error`

`Error` fits the library's words (`Ok`, `Fail`, `TError`, `MapError`, `TeeError`), and it collides.

| Package | Its type | Verified |
|---|---|---|
| OneOf | `OneOf.Types.Error` (and `Error<T>`) | CS0104 when both namespaces are imported |
| ErrorOr | `ErrorOr.Error` | CS0104 |
| FluentResults | `FluentResults.Error` | CS0104 |
| LanguageExt | `LanguageExt.Common.Error` | read from source, not compiled |
| .NET 11 and ASP.NET Core 11 | no public top-level `Error`, `Fault`, `Failure`, `Problem`, `Reason`, `Cause` or `Issue` | metadata scan |

OneOf matters most, since Resultful users import `OneOf.Types` until the core drops OneOf (roadmap M3). One line
fixes it (**verified** for OneOf and ErrorOr), and the README would show it:

```csharp
global using Error = Resultful.Error;
```

Not chosen: **`Fault`** (suggests a defect, not an expected failure), **`Failure`** (ErrorOr and Ardalis use it
for a category), **`Problem`** (collides in meaning with ProblemDetails, which `Resultful.AspNetCore` produces).
Third-party packages beyond those in the table weren't checked for these.

## Alternatives

- **Keep strings.** Fails the first and second points of the test.
- **One result, with an aggregate inside `Error`.** An earlier draft of this document: `Error` combined with `+`
  into an `AggregateError`. It hid the count inside the value, so a signature couldn't say whether it gathers
  errors, and context over many errors had no clear meaning. Replaced by the split.
- **One result, two error types** (`Result<T, Error>` and `Result<T, Errors>`). Reuses `IPlus`, but `Error` and
  `Errors` are one letter apart and `Bind` would still exist on the plural form.
- **No built-in error; satellites take callbacks.** Works, but every user writes the same context and mapping.
- **An `IError` interface.** Would allow struct errors, but records give equality and `ToString` for free, and
  there's no operator left that needs static interface members.

## What changes for users

A break, shipped with the others in `0.3.0-beta01` ([decision 8](../decisions/0008-version-0-3-0.md)).

- **69 lines** of `PublicAPI.Shipped.txt` mention `IEnumerable<string>`. Each becomes `Error` (on `Result<T>`) or
  moves to `Validation<T>`.
- **Code that gathers errors with `Result<T>.Plus`** moves to `Validation<T>` and `&`.
- **`Result<string> r = "message"` flips from failure to success, silently.** Today the string converts to an
  error. With `Error` it converts to the value, because C# doesn't chain user-defined conversions. The analyzer
  rule planned for M4 ("`Result<string>` built from a bare string") has to ship in the same release.
- **`Result<int> F() => "bad";` stops compiling** (CS0029, **verified**). It becomes `Fail<int>("bad")` or
  `(Error)"bad"`. A compile error is the safe kind of break.
- **`string[]` and `List<string>` conversions** move to `Validation<T>`.
- **`Result<Error>` is ambiguous** (CS0457, **verified**). It's rare, and `Ok`/`Fail` still work.
- **Allocation is about the same.** A failure allocates a record (or a list for `Validation`) where it allocated an
  array before. Success doesn't allocate.
- **`Result<T, TError>` doesn't change** for users with their own error type.

The core still has no dependencies. Records on `netstandard2.0` need `IsExternalInit` and `NotNullWhenAttribute`,
which the core declares internally (**verified** with the prototype).

## If decision 14 is accepted

- Roadmap M5's "structured `Error` type… additive" item is replaced: `Error` and `Validation` move to M3 with the
  other breaks.
- Roadmap M2's accumulating operators (`+ - *`, `&`) belong to `Validation`. On `Result` they short-circuit, or
  don't exist. This changes the operator table in [decision 9](../decisions/0009-operators-are-opt-in-sugar.md).
- The architecture's core row lists `Validation` and `Error`.
- `Resultful.AspNetCore` maps `Validation<T>` to a validation problem and `Result<T>` by error type.
- `Resultful.FluentValidation` returns `Validation<T>`.
- The [seamless exploration](seamless-with-dotnet.md)'s structured-error item (section 6) is superseded.

## Still open

- **The name of the plural type.** `Validation` is the functional idiom; whether it's the right name for
  Resultful is undecided.
- **Two readings of `Validation<T, TError>`.** This document builds a new type with an `IReadOnlyList<TError>`.
  The lighter reading keeps today's `Result<T, IEnumerable<TError>>` machinery and gives it the name
  `Validation`, so only `Error` is new.
- **`Bind` on `Validation`.** None (Haskell, Cats), or one that stops at the first failure (FsToolkit).
- **Many to one inside a `Result` chain.** Whether the core offers a built-in error that holds a list and that
  `TryFind` searches, or `ToResult` also returns `Result<T, IReadOnlyList<TError>>` as FsToolkit does, or the user
  always supplies `combine`.
- Whether `Validation` keeps `IPlus`-style `+` on values (`a + b` adding two successful numbers), or only `&`.
- When `IPlus` and `Result<T, TError>.Plus` are deprecated.
