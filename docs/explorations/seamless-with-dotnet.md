---
type: exploration
title: "Seamless with .NET"
description: "How Resultful could feel native to C# and .NET: extension operators, unions, nullable flow, async, and the framework seams (ASP.NET Core, JSON, validation, analyzers, testing, observability)."
status: draft
tags: [resultful, exploration, language, ecosystem]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Seamless with .NET

> Draft exploration, not a plan. It goes beyond the [roadmap](../roadmap.md) on purpose. Ideas that survive
> review become decisions; the rest stay here with the reason they were dropped. Claims marked **verified** were
> compiled and run on SDK 11.0.100 against the current `Resultful` types (`LangVersion latest`, consumer on
> `net11.0`). Everything else is design.

## What "seamless" means

A result type feels foreign when the code around it has to change shape to use it: lambdas where C# would use an
operator, `Match` where C# would use `if` or `switch`, adapters at every framework boundary, warnings the compiler
can't give. Resultful is seamless when each of these seams disappears:

1. **Language**: operators, patterns, `switch`, nullable flow and (on .NET 11) unions treat a result like a
   built-in type.
2. **Async**: a `Task<Result<T>>` reads like a `Result<T>`.
3. **Framework**: ASP.NET Core, `System.Text.Json`, validation, logging and tracing take results directly.
4. **Tooling**: the compiler and analyzers catch an ignored or unchecked result; tests assert on results directly.

The [vision](../vision.md) sets the limits: idiomatic over pure, nothing to inherit, additive by default.

## 1. Extension operators (C# 14)

C# 14 *extension members* let a library add properties, static members and **operators** to types it doesn't
own, including `Task<T>` and generic instantiations. That turns the railway into syntax.

### Verified on the current types

| Expression | Meaning | Result | Built on |
|---|---|---|---|
| `a + b`, `a - b`, `a * b`, `a / b` | Combine two successes with the value's own operator, or **accumulate both sides' errors** | `2.Ok() + 3.Ok()` is `Ok(5)`; `"nope" + "also"` is `Fail(nope, also)`; `a * b - a` composes | `Plus(…, func)` plus generic math (`IAdditionOperators` etc.) |
| `a * 10` | Scale a success by a plain value | `Ok(20)` | `Map` |
| `s += b` | Compound assignment, derived from `+` | `Ok(5)` | `+` |
| `bad \| a` | Fallback: first success wins | `Ok(2)` | new `OrElse` |
| `bad \| 42` | Unwrap with a default | `42` (a `T`, not a result) | `Match` |
| `bad \|\| Expensive()` | **Lazy** fallback: the right side runs only on failure | `a \|\| Expensive()` never calls `Expensive` | `\|` plus `true`/`false` |
| `a >> (x => …)` | Bind, including type-changing `Result<int> >> Result<string>` | `Fail(too big)`, `"20"` | `Bind` |
| `a & b` | Applicative combine into a tuple, accumulating errors | `Ok((2, "x"))` | `Plus` |
| `Result.Ok() & "x" & Result.Fail("y")` | Gather every `VoidResult` error | `Fail(x, y)` | `Plus` |
| `if (a)`, `!bad` | Truthiness | as expected | `true`/`false`/`!` |
| `Result<int>.Success(7)`, `Result<int>.Failure("e")` | Static extension factories on the type itself | as expected | `Ok` |
| `a is { IsSuccess: true }` | Extension property in a property pattern | `true` | `Match` |
| `await (task \| 99)`, `await (task >> (async x => …))` | The same operators on `Task<Result<T>>`, with async steps | `99`, `"3"` | async helpers |
| `option \| 4` | `Option<T>` default value | see the `default` hazard below | `Match` |

Extension members compile in the `netstandard2.0` build of the library. A consumer needs C# 14, the default on
`net10.0` and later. The generic-math operators (`+ - * /` on the value) need `NET7_0_OR_GREATER`.

### Rules the probe forced

- **One `>>` only.** With both a same-type and a type-changing `>>`, `a >> (x => …)` is ambiguous (CS9342). The
  type-changing form `extension<T, U>(Result<T>)` covers both, because `U` is inferred from the lambda.
- **`&` means one thing.** `&&` needs a *same-type* `&`, while the useful `&` is the tuple combine. Pick the tuple
  combine and give up `&&`. Laziness is still available through `||`.
- **`/` is not safe.** `1.Ok() / 0.Ok()` throws `DivideByZeroException`. Either `/` is left out, or it is
  documented as the value's own operator: Resultful doesn't turn bugs into failures (see the vision).
- **Every operator is sugar over a named method.** Consumers on older C# compilers, and other .NET languages,
  can't see extension operators. `Plus`, `Bind`, `Map` and a new `OrElse`/`ValueOr` are the API; operators are
  spelling.
- **Precedence is C#'s.** `a | b >> f` parses as `a | (b >> f)`. A lambda on the right of `>>` needs parentheses.
  The docs need a precedence table, and the examples should use parentheses.

### Open questions for the maintainer

- **Truthiness and `>>` pull against "explicit over hidden".** `if (result)` hides which case you're in, and `>>`
  for bind is Haskell's spelling, not C#'s. One answer: put the operators in an opt-in namespace,
  `Resultful.Operators` ([decision 9](../decisions/0009-operators-are-opt-in-sugar.md)), so the default surface
  stays named methods.
- **`|` returning `T` is convenient but changes type silently.** `bad | 42` is an `int`, `bad | a` is a result.
  The alternative is to keep `|` result-to-result and spell unwrapping `ValueOr(42)`.
- **Should `+` on two failures accumulate?** The probe accumulates, which matches `Plus` today. Short-circuiting
  would match `>>`. Accumulating is the useful choice, because `>>` already short-circuits.

### Collections and keyed merge (verified, undecided)

`+` and `-` also work on results of collections. On success the values combine, and on failure the errors
accumulate.

| Expression | Result |
|---|---|
| `[1,2].Ok() + [3].Ok() + Parse("4")` | `Ok(1 2 3 4)` |
| `acc += Parse(s)` over `"1","x","3","y"` (`ImmutableList`) | `Fail('x' …, 'y' …)`: every value or every error |
| `list + Parse("1") + Parse("2") - 1` | `Ok(2)`: `-` uses the element's equality |
| `{a:1} + {b:2} + ("c",3) - "a"` | `Ok({b:2, c:3})` |
| `{a:1} + {b:2} + ("a",9)` | `Fail(duplicate key a)` |
| case-insensitive `{Apple:1} + {APPLE:2}` | `Fail(duplicate key APPLE)`: the dictionary's comparer decides |
| `{apple:1, pear:2} * {APPLE:5}` | `Ok({apple:6, pear:2})`: on a clash the values combine with `V`'s own `+` |
| case-insensitive set `{a,B} + {b,c} - {A}` | `Ok({B, c})` |
| `Result<List<T>>` `+=` | C# 14 user-defined compound assignment, appending in place |

An operator can't take a comparer, so equality travels with the data: the collection's comparer for keys, the
element type's equality for elements, and the value type's `+` for clashes.

Open points:
- **Which comparer wins.** The left side's comparer is used, so `a + b` can differ from `b + a`.
- **Two meanings for `+` on dictionaries.** Should `+` fail on a clash and `*` merge (as probed), or should `+`
  merge whenever `V` supports `+`?
- **Key casing.** A merge should keep the left key's casing, but the probe kept the right's.
- **Aliasing.** In-place `+=` on a `List` is visible to anyone else holding the same list.
- **Targets.** The immutable collections are in-box only on `net10.0` and later.
- **Scope.** Operators on bare collections stay out, because Resultful is not a collections library.

## 2. Hazards the probes found

These exist today, independent of operators. Operators make them easier to hit.

### `default` is success

**Verified.** `default(Result<int>)` is `Ok(0)`, `default(VoidResult)` is `Ok`, `new Result<string>[1][0]` is
`Ok(null)`, and `default(Option<int>)` is `Some(0)`. An uninitialised field, an array element or a missing
deserialised value is silently a success. The cause is the OneOf storage, whose case 0 is the success case.

The dependency-free storage change in roadmap M3 is the moment to fix it: `default` becomes a distinct
uninitialised state that throws on use, `new Result<T>()` stops compiling, and an analyzer catches the rest
([decision 10](../decisions/0010-default-is-not-success.md)).

### A string converts to an *error*

**Verified.** `Result<string> rs = "hello";` is a failure, because `Result<T>` has an implicit conversion from
`string` to the error case. `Result<string> | "fallback"` happens to resolve to the `T` overload, but a reader
can't tell. `Result<int, int> x = 3;` is a compile error (CS0457), which is at least safe.

Options:
- Keep the conversion and add an analyzer warning on `Result<string>` built from a bare string.
- Move message errors to a structured `Error` type (section 6) so that `string` stops being special.
- On .NET 11, use a wrapper case type so the union cases never overlap (section 3).

### The API surface differs by target

The generic-math operators exist only on `net10.0`/`net11.0`. The `PublicAPI.*.txt` files added in #24 assume one
surface, so they need per-target files (`PublicAPI/netstandard2.0/…`) or a separate baseline for the NET7+
members.

## 3. Unions on .NET 11

C# 15 `union` declarations and custom unions are in .NET 11 (not preview). The research in roadmap M6 points
to a **custom union** over a compiler-generated `union`:

- `[Union]` on `Result<T>` with a nested member provider (`IUnionMembers`) whose static `Create` methods allow
  private constructors.
- A non-boxing `HasValue` and `TryGetValue(out T)` / `TryGetValue(out TError)`, which the compiler uses for type
  patterns and nullable flow, so `IUnion.Value` (boxed) is never on the hot path.
- Then `result switch { int v => …, Error e => … }` is exhaustive with no `Match`.

**Overlapping cases** (`Result<string, string>`, `Result<object>`, `Result<List<string>>` against the
`IEnumerable<string>` error case) make type patterns ambiguous. Wrapper case types (`Ok<T>`, `Fail<TError>`) fix
that at the cost of one more name in the pattern. The experiment in decision 3 decides, with a correctness matrix
before any benchmark.

## 4. Nullable flow and patterns (every target)

Cheap, and it works without unions:

- `IsSuccess` / `IsFailure` with `[MemberNotNullWhen]` so `Value` is non-null after a check.
- `TryGetValue([NotNullWhen(true)] out T value)` and `TryGetError(...)`, the `Dictionary.TryGetValue` idiom
  every C# developer already knows.
- `Deconstruct(out bool ok, out T? value, out TError? error)` for `var (ok, value, error) = result;`.
- Extension properties (verified above) make `IsSuccess` work in property patterns without changing the struct.

The attributes are polyfilled internally on `netstandard2.0`.

## 5. Async

- Prefer `ValueTask` on new overloads, and avoid a sync × async × `Task`-receiver overload explosion: the
  `Task<Result<T>>` operators above cover the receiver case with one definition each.
- `IAsyncEnumerable<Result<T>>` helpers (`Sequence`, `Partition`) for streaming pipelines.
- `CancellationToken` flows through every async step that takes one.

## 6. The framework seams (separate packages)

Each is a separate package so that the core stays dependency-free ([decision 1](../decisions/0001-core-is-dependency-free.md)).
Ranked by how much friction each removes:

1. **`Resultful.AspNetCore`.** `result.ToHttpResult()` returns `Results<Ok<T>, ProblemHttpResult>`, so OpenAPI
   sees both cases, with RFC 9457 ProblemDetails through `IProblemDetailsService`, a configurable
   error-to-status map, and an endpoint filter so a Minimal API handler can simply return `Result<T>`.
2. **A structured `Error`** (in the core, opt-in): `Code`, `Description`, `Type` (validation, not found,
   conflict, unexpected) and `Metadata`, with no HTTP coupling. `Type` is what the AspNetCore map reads. It also
   removes the string-as-error ambiguity.
3. **`Resultful.Analyzers`** (shipped inside the core package as an analyzer asset, no runtime dependency):
   - a result that is never observed
   - `Value` used without a check
   - a `Match` that should be a `switch`
   - `Result<string>` built from a bare string
   - `default(Result<T>)`
4. **`System.Text.Json` and AOT.** A converter factory with a source-generation-friendly shape, plus
   `IsAotCompatible` and `IsTrimmable` on the core, checked by a native-AOT smoke app in CI. No competing library
   advertises AOT, which makes this a differentiator.
5. **Validation.** A FluentValidation adapter (`validator.ValidateToResult(x)`) and a bridge to the built-in
   .NET 10 Minimal API validation.
6. **Assertions** live in a separate repository, `Resultful.Assertions`: an independent core plus
   adapters for FluentAssertions, AwesomeAssertions and others ([decision 12](../decisions/0012-assertions-live-in-resultful-assertions.md)).
7. **Observability.** Optional `ILogger` and `ActivitySource` hooks: a failure tags the current activity with an
   error code, and nothing is logged by default.
8. **Samples.** A MediatR pipeline behaviour, Polly (`ShouldHandle` on a failed result) and EF Core, as samples
   rather than packages.

## 7. A possible sequence

All additive unless marked otherwise. Each is one pull request after roadmap M1 (foundations), which pins today's
behaviour first.

1. Nullable-flow members, `TryGetValue`, `Deconstruct` and named `OrElse`/`ValueOr`, on every target.
2. `Resultful.Operators`: the operator set above over those named methods, with a precedence table and
   per-target public API files.
3. The dependency-free storage change (roadmap M3), with `default` no longer a success. **Breaking.**
4. Structured `Error`, then `Resultful.AspNetCore`.
5. Analyzers, then the JSON converter and the AOT smoke test.
6. The .NET 11 union experiment (roadmap M6), now with the overlap cases in its matrix.
7. Validation and observability packages; samples. Assertions follow in `Resultful.Assertions`.

## Probe

The probes live in the gitignored `temp/seamless-probe` (a `net11.0` console app referencing
`Resultful.csproj`). Anything adopted from them is rewritten with tests.
