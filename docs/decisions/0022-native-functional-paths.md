---
type: decision
title: "Native C# paths for complicated binding steps"
description: "Early return with Failed, async query syntax, collection and async-stream operators, Ensure, and an await short-circuit probe, as small additive PRs on top of stack B."
status: stable
tags: [resultful, decision, language, async]
decided:
  by: human:rtkelly13
  at: 2026-11-21
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# 22. Native C# paths for complicated binding steps

> The options, and those rejected, are in
> [Functional style with native C#](../explorations/native-functional-csharp.md).

Complicated binding steps (several dependent results, async calls, loops, early exits) should stay plain C#.
Pipelines remain the main style; these are the native alternatives. All of it is additive.

## Decided

1. **Early return.** `Failed(out value, out error)` on every result type and `Failed(out value, out errors)` on
   `Validation`, annotated so nullable flow knows which is set. A zero value throws. RF0001 counts it as observing
   the result and RF0003 as the check; a new rule flags using a value-type `value` inside the failed branch.
2. **`return error;` for typed results.** The `TError` → `Result<T, TError>` conversion already exists in 0.3
   (it was missed when the exploration was written); 24b documents and tests it with `Failed`.
3. **Async query syntax.** `Select`/`SelectMany` over `Task<Result<…>>` and `ValueTask<Result<…>>`, mixed with sync
   steps. Query syntax on generated shapes keeps the shape's type. `Result.Ensure(condition, error)` is the step
   for a condition, with `[CallerArgumentExpression]` for a default message on `Result<T>`. No `where`, no query
   syntax over `Validation`, no queries mixing `IEnumerable` and `Result`.
4. **Collections (sync).** `Traverse` and `Collect` (first failure in input order) and `TraverseAll` and
   `CollectAll` (every failure, in input order) over `IEnumerable`; `params ReadOnlySpan` overloads of
   `Validation.Combine`. Not `Sequence`: decision 4's `Result.Sequence(steps…)` already means "run a chain". Async
   traversal is item 5's.
5. **Async streams and all async traversal** (`net10.0`+, in the core behind `#if`). On
   `IAsyncEnumerable<Result<T>>`: `SelectResultAsync`, `Successes`, `Failures`, `CollectAllAsync` (`CollectAsync` and
   `PartitionAsync` already shipped in 19); `TraverseAsync` and `TraverseAllAsync` over `IAsyncEnumerable<T>` with a
   concurrency limit (an `IEnumerable` source converts with .NET 10's `ToAsyncEnumerable()`). `Resultful.AspNetCore` answers `Result<IAsyncEnumerable<T>>` with ProblemDetails before the
   first item and streams after it. Exceptions stay exceptions; cancellation reaches the source.
6. **A probe, not a feature:** `await` on a result as a short-circuit through a method-level async builder. It
   ships only through a later decision, with the probe's findings.
7. **A guide page**, "Choosing a style", with one Shop handler per style.

## Delivery

Small PRs on top of stack B, after 20c and before 24 (core unions), so the core is finished before the Shop (23a)
uses every style; none of them needs `[Union]`:

| PR | Contents |
|---|---|
| 24b `early-return` | items 1 and 2 |
| 24c `comprehensions` | items 3 and 4 |
| 24d `async-streams` | item 5 |
| 24e `await-probe` | item 6: a probe and a findings note, no package API |

Each has CsCheck laws against `Match` (and, for collections and streams, against the materialised list),
laziness and cancellation tests, and the analyzer cases.

## Open

- A `[CollectionBuilder]` for validation failures (`Validation<T> v = [err1, err2];`) may not be expressible for a
  type that holds either a value or errors; checked in 24c.
- Static abstract `IResult<TSelf, T, TError>` to write these once for core types and shapes: explored in 24c,
  adopted only if it doesn't widen the public surface much.
- Extension operators on `Task<Result<T>>` (C# 14) for decision 9's operators: considered with 18's follow-ups.

Why:
- Each gap found in the exploration (no `TError` conversion, maybe-null values after `Deconstruct`, query syntax
  that breaks on one async step, no shape queries, nothing for streams) pushes users back to `Match` and `!`.
- All of it uses only the language and in-box APIs, so the core stays dependency-free.
