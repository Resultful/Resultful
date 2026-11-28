---
type: decision
title: "Steps as an iterator over a result shape"
description: "Yield gathers every error from an iterator of shape steps and YieldFirst stops at the first; yield return takes the success or any leaf with no wrapping. Additive, prototyped before it ships."
status: proposed
tags: [resultful, decision, language, shapes]
decided:
  by:
  at:
generated:
  by: claude-opus-5.5
  at: 2026-11-28
---

# 24. Steps as an iterator over a result shape

> The probe, the comparison with 24b/24c and the costs are in
> [Steps as an iterator](../explorations/iterator-steps.md).

An iterator whose element type is a result shape (`IEnumerable<CustomerResult>`) can `yield return` the success or
any error leaf, inner leaves of a nested union or sub-hierarchy included, with no wrapping. Two drivers read it.
All of it is additive; no existing member changes.

## Proposed

1. **`Yield(steps)`** runs every step and returns `Validation<T, TError>`: the success if it is the only thing
   yielded, otherwise every error in the order yielded. For gathering errors from steps of different types, branches
   and loops, where `Combine` and `TraverseAll` (24c) need one element type or nesting.
2. **`YieldFirst(steps)`** returns the shape: the first error, or the one success. It stops reading at the first
   error and disposes the iterator, so `finally` and `using` in it run. `Failed` (24b) stays the documented
   fail-fast style; `YieldFirst` is for an iterator that is also read by `Yield`.
3. **Run-time rules.** No error and zero successes, or a second success, throws `InvalidOperationException`. A zero
   value step throws (decision 10).
4. **An analyzer, RF0012**, warns when an iterator of shape steps is not passed straight to a driver: dropped,
   stored, or enumerated twice (it is lazy; a second read repeats every side effect).
5. **Docs:** a "Choosing a style" section (the guide page proposed on #84) with one Shop handler per driver.

## Open (for the maintainer)

- **Generated or generic.** Generated: `CustomerResult.Yield(…)` / `CustomerResult.YieldFirst(…)` on each shape;
  no type arguments, and a `ManyErrors` shape can return itself; costs two members per shape in
  `emitted-api-budgets.txt`. Generic: one pair in the core over `IEnumerable<Result<T, TError>>`, usable without
  shapes; on `net11.0` an inner union leaf may not convert to the element type (C# chains one user-defined
  conversion). A shared static-abstract shape interface (an open item on #84) would allow a third form.
- **A checkpoint.** Gather input errors, then stop before dependent steps, in one iterator. The zero value can't be
  the sentinel (decision 10), so it needs a distinct marker or two iterators.
- **Shapes without `Success`.** Whether zero successes then means success, so no trailing `yield return Unit.Value`.
- **Async.** `YieldAsync`/`YieldFirstAsync` over `IAsyncEnumerable<shape>` (`net10.0`+): `await` inside the steps,
  `DisposeAsync` on the first error, a cancellation token. Not probed.

## Delivery

| PR | Contents |
|---|---|
| this | the exploration and this proposal; docs only |
| next | a prototype on the Shop: both drivers and RF0012, CsCheck laws against `Combine` and `Failed`, disposal and laziness tests, on `net10.0` and `net11.0`. It takes whichever form the maintainer chooses above |

Why:
- The Shop showed the shape's conversions make leaves cheap to return but not cheap to gather: gathering mixed
  steps still means `Combine` arities or a hand-kept list.
- The iterator is plain C# on every target, works the same on closed hierarchies and unions, and keeps the core
  dependency-free.
- The costs (laziness, the extra `yield break`, run-time success counting) are real, so it ships only after a
  prototype and an analyzer.
