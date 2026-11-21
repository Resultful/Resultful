---
type: exploration
title: "Functional style with native C#"
description: "Every C# language path for writing result-based code without leaving the language: early returns, query syntax, collections, async streams, await as a short-circuit, patterns, unions and newer language features; what Resultful has, what it could add, and what it should not."
status: draft
tags: [resultful, exploration, language, async]
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# Functional style with native C#

> Draft exploration, not a plan. Ideas that survive review go into
> [decision 22](../decisions/0022-native-functional-paths.md); the rest stay here with the reason. It follows
> [Seamless with .NET](seamless-with-dotnet.md) and assumes the 0.3 core (stack B): named methods, the `Error`
> record, generated shapes, closed hierarchies and, on .NET 11, error unions.

## The problem

Short pipelines read well as `Bind`/`Map` chains. Complicated binding steps don't: several dependent results, a
mix of sync and async calls, a loop, a `try` or a `using`, an early exit. The chain fills with nested lambdas, or
the code falls back to `Match` and `!`. Each path below keeps such code in plain C#.

## What 0.3 already has

| Path | In the core today |
|---|---|
| Named checks | `IsSuccess`/`IsFailure` with `[MemberNotNullWhen]`, `TryGetValue(out T)`, `TryGetError(out TError)` |
| Deconstruction | `var (ok, value, error) = result;` |
| Query syntax | `Select`/`SelectMany` for `Result<T>`, `Result<T, TError>` and `Option<T>` (`Resultful.LINQ`) |
| Accumulation | `Validation.Combine` up to eight values |
| Sequencing | `Then`, `Check`, `Sequence` (the last only over `VoidResult` steps, which are obsolete) |
| Conversions | `Error` → `Result<T>`, `T` → `Result<T>`, `TError` → `Validation<T, TError>`, leaves → shapes |
| Patterns | exhaustive `switch` over closed hierarchies (the RFSP0001 suppressor) and, on .NET 11, error unions |
| Operators | opt-in `| || >> & + - *` over the named methods ([decision 9](../decisions/0009-operators-are-opt-in-sugar.md)) |

Gaps found while writing this:
- (Corrected later: `Result<T, TError>` does already convert implicitly from `TError`, so `return error;` works for
  typed results too.)
- `Deconstruct` gives a value the compiler still treats as maybe-null after `if (!ok) return error;`: nullable flow
  doesn't link tuple elements.
- One async step breaks query syntax: there are no overloads over `Task<Result<…>>` or `ValueTask<Result<…>>`.
- Generated shapes get no `Select`/`SelectMany`, so query syntax doesn't work on them.
- Nothing works on `IAsyncEnumerable<Result<T>>`, although the roadmap lists it under M2.

## Path 1. Early return (Go style)

```csharp
if (result.Failed(out var order, out var err))
    return err;

return DoSomething(order);
```

- `Failed([MaybeNullWhen(true)] out T value, [NotNullWhen(true)] out TError? error)` on every result type, and
  `Failed(out value, out errors)` on `Validation`. Nullable flow knows `err` is set in the branch and `order` after
  it. No negation to miss, unlike `if (!r.TryGetValue(…))`.
- The name can't be `IsFailure(…)`: that's the property. `out` order is value then error, like Go's
  `(value, err)` and the existing `Deconstruct`.
- `return err;` needs the missing `TError` → `Result<T, TError>` conversion. Its cost: an ambiguity error at the
  call site when `T` and `TError` are the same type.
- A zero value throws, like the other `TryGet*` members ([decision 10](../decisions/0010-default-is-not-success.md)).
- For a value-type `T` the compiler can't warn when `order` is used inside the failed branch (it's just `0`). An
  analyzer rule can.
- RF0001 counts `Failed` as observing the result; RF0003 counts it as the check.
- Async needs brackets: `if ((await GetOrder(id)).Failed(out var order, out var err)) return err;`. An async method
  can't have `out` parameters of its own, so there's no way round them.

Rejected: a Go-style `(T value, Error? err)` tuple return. It brings back "zero means success".

## Path 2. Query syntax (comprehensions)

```csharp
var receipt =
    from order   in ParseOrder(dto)
    from stock   in inventory.ReserveAsync(order)     // Task<Result<Stock>>
    let  total   = pricing.Total(order)
    from _       in Result.Ensure(total > 0, new EmptyOrder())
    from payment in payments.ChargeAsync(order.Card, total)
    select new Receipt(order.Id, payment.Id);          // Task<Result<Receipt>>
```

- Add `Select`/`SelectMany` over `Task<Result<…>>` and `ValueTask<Result<…>>`, and the mixed sync/async pairs, so
  any step can be async. The query's type becomes a task once one step is.
- Add `Select`/`SelectMany` to generated shapes, or to a generic `IResultShape<T, TError>` path, so queries keep
  the shape's type.
- `Result.Ensure(condition, error)` returning `Result<Unit>` (and the `TError` form), with
  `[CallerArgumentExpression]` so `Ensure(total > 0)` on `Result<T>` can default its message to `"total > 0"`. It
  records code text, never values, so it leaks no personal data.

Rejected:
- `where`: a failed predicate needs an error and `where` has nowhere to put one; for `Result<T, TError>` it can't
  be built at all. `from _ in Ensure(…)` is the replacement.
- Query syntax over `Validation`: in `from a … from b …` each step can depend on the last, so it must stop at the
  first failure, the opposite of accumulation. `Validation.Combine` stays the way to accumulate.
- Queries mixing `IEnumerable` and `Result`: two monads in one query fight over overloads. `Traverse` covers it.

## Path 3. Collections

- `items.Traverse(f)` → `Result<IReadOnlyList<U>>`, stopping at the first failure; `results.Collect()` for a list
  that already holds results. Accumulating forms `TraverseAll`/`CollectAll` → `Validation<IReadOnlyList<U>>`. (Not
  `Sequence`: decision 4's `Result.Sequence(steps…)` already means "run a chain".)
- `params ReadOnlySpan<…>` (C# 13) overloads of `Validation.Combine` for any number of same-typed values, beyond
  the eight-argument overloads.
- A collection expression for a validation failure, `Validation<T> v = [err1, err2];`, through
  `[CollectionBuilder]` (to check: a builder for a type that is either a value or errors may not be allowed).
- The roadmap's other collection operators (results of arrays, immutable lists, sets and dictionaries with the
  collection's comparer) stay in exploration.

## Path 4. Async streams

On `net10.0` and later only. `IAsyncEnumerable` and `System.Linq.AsyncEnumerable` are in-box there; the core
references nothing ([decision 1](../decisions/0001-core-is-dependency-free.md)), so `netstandard2.0` doesn't get
them ([decision 11](../decisions/0011-package-family.md): in-box features on newer targets go in the core behind
`#if`). Only result-aware operators are added; .NET 10's async LINQ does the rest.

A stream of results, `IAsyncEnumerable<Result<T>>` (a queue worker, a file import):
- `SelectResultAsync(f)` maps or binds each item; failures stay in the stream as items.
- `Successes()`, `Failures()`, `PartitionAsync()`.
- `CollectAsync()` → `Result<IReadOnlyList<T>>` (already in 0.3, from 19), stopping at the first failure. It stops pulling and disposes the
  source, so the producer is cancelled. `CollectAllAsync()` → `Validation` with every failure.
- `TraverseAsync(source, f, maxConcurrency, preserveOrder, ct)`; on the first failure a linked token cancels the
  work in flight.

A result that holds a stream, `Result<IAsyncEnumerable<T>>`: the checks run before the first item.
- `Resultful.AspNetCore`: a 404 or 400 ProblemDetails before any header, then streamed JSON. After the first item a
  failure can only end the response; the docs say so.
- `Resultful.HotChocolate`: the same split for subscriptions and `@stream`.

Native C# that already works with Path 1:

```csharp
await foreach (var r in orders.SelectResultAsync(Handle, ct))
{
    if (r.Failed(out var receipt, out var err)) { deadLetter.Add(err); continue; }
    await Publish(receipt, ct);
}
```

Rules: an exception from the producer stays an exception; cancellation always reaches the source; no query syntax
mixing streams and results. The Shop's worker (UC6) is the real-use scenario.

## Path 5. `await` as a short-circuit

The closest C# gets to Rust's `?` or Haskell's `do`:

```csharp
[ResultBuilder]
async Result<Receipt> PlaceOrder(OrderDto dto)
{
    var order   = await ParseOrder(dto);           // a failure ends the method here
    var stock   = await inventory.Reserve(order);
    var payment = await payments.Charge(order.Card, pricing.Total(order));
    return new Receipt(order.Id, payment.Id);
}
```

- A `GetAwaiter` on results whose awaiter never resumes on failure, and a method-level `[AsyncMethodBuilder]`
  (C# 10) whose builder returns the failure. Values are non-null; loops, `try` and `using` work.
- Costs: it looks async and isn't; a state machine per call; real `await`s need a second builder over
  `ValueTask<Result<T>>`; analyzers must understand it; debuggers show an async stack.
- Unproven. A probe decides whether it ships, and it would get its own decision.

## Path 6. Patterns, tuples and unions

- On .NET 11, with `[Union]` on the core types (stack B 24), tuple patterns combine results without `Bind`:

  ```csharp
  (orderResult, stockResult) switch
  {
      (Order o, Stock s)             => Ship(o, s),
      (Error e, _) or (_, Error e)   => e,
  }
  ```

- Target-typed conditionals already work where conversions exist:
  `return stock > 0 ? item : new OutOfStock();` (needs the `TError` conversion for typed results).
- Property patterns over the named members: `if (result is { IsFailure: true }) …` reads, but gives no access to
  the error without `TryGetError`. `Failed` is the better form.

## Path 7. Newer language features

- **Extension members (C# 14)**: extension properties such as `IsSuccess` on `Task<Result<T>>`; extension operators
  so decision 9's operators work on tasks without wrapper types.
- **Static abstract interface members**: `IResult<TSelf, T, TError>` with static `Ok`/`Fail` lets `Traverse`,
  `Failed`, query syntax and the assertions be written once for the core types and every generated shape.
- **`using static Resultful.Result;`** for function-like `Ok(x)`/`Fail(e)`. Docs only.
- **Static lambdas and method groups** keep pipelines free of hidden captures. Docs, maybe an analyzer hint.

## Not available, or best avoided

- A pipe operator (`|>`): C# has none. Extension methods are the native pipe; `>>` covers bind.
- Overloading `?.` or `??`: not possible. `ValueOr` and `OrElse` are the named forms.
- Active patterns: none in C#. Unions and `[MemberNotNullWhen]` members are the nearest.
- Interceptors to rewrite LINQ: experimental, too fragile for a library.

## Which to use when (for the guide)

| Code | Style |
|---|---|
| A short linear chain | `Bind`/`Map`, or query syntax |
| Dependent steps, some async | Query syntax (Path 2) |
| Branching, loops, `try`/`using`, early exits | `Failed` (Path 1), or `await` (Path 5) if it ships |
| Independent checks, every error wanted | `Validation.Combine`, `TraverseAll` |
| A list of inputs | `Traverse` / `TraverseAll` |
| A stream | the async-stream operators, `await foreach` + `Failed` |
| Choosing on the error | `switch` over the hierarchy or union |
