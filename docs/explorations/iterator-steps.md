---
type: exploration
title: "Steps as an iterator"
description: "A C# iterator whose element type is a result shape: yield return takes the success or any error leaf with no wrapping, and a driver either gathers every error or stops at the first."
status: draft
tags: [resultful, exploration, language, shapes]
generated:
  by: claude-opus-5.5
  at: 2026-11-28
---

# Steps as an iterator

> Draft exploration behind [decision 24](../decisions/0024-iterator-steps.md), which is **proposed**. The probe
> below was run by hand on `net10.0` and `net11.0` against the Shop's `CustomerResult`; the prototype PR re-proves
> it with tests.

## The idea

A method returning `IEnumerable<CustomerResult>` can `yield return` anything that converts to the shape: the
success (`Customer`), any leaf (`CustomerNotFound`), or an inner leaf (`Suspended`, through the nested union or
sub-hierarchy), with no `Fail(...)` or wrapping. A driver then reads the steps:

- **`Yield`** runs every step and returns `Validation<Customer, CustomerError>`: the success if it is the only thing
  yielded, otherwise every error, in order.
- **`YieldFirst`** returns the shape: the first error, or the one success. It stops reading at the first error, and
  `foreach` disposes the iterator, so the iterator's `finally` and `using` blocks run.

```csharp
// Every delegate must be an active customer; the owner comes back only if nothing failed.
static IEnumerable<CustomerResult> Owner(string id, IEnumerable<string> delegates)
{
    foreach (var d in delegates)
    {
        var found = Customers.Find(d);
        if (!found.TryGetValue(out Customer? _)) yield return found;
    }
    yield return Customers.Find(id); // its success or its error
}

Validation<Customer, CustomerError> owner = CustomerSteps.Yield(Owner("C-1", ["C-2", "C-3"]));
// fails with Suspended(C-2), Unverified(C-3)
```

```csharp
// Fail fast: stop at the first error, and the scope is still disposed.
static IEnumerable<CustomerResult> Transfer(string from, string to, Audit audit)
{
    using var scope = audit.Begin();
    var source = Customers.Find(from);
    if (!source.TryGetValue(out Customer? s)) { yield return source; yield break; }
    var target = Customers.Find(to);
    if (!target.TryGetValue(out Customer? t)) { yield return target; yield break; }
    yield return t;
}

CustomerResult target = CustomerSteps.YieldFirst(Transfer("C-1", "C-4", audit)); // CustomerNotFound(C-4)
```

The probe's drivers, written by hand in place of generated members:

```csharp
public static class CustomerSteps
{
    public static Validation<Customer, CustomerError> Yield(IEnumerable<CustomerResult> steps)
    {
        List<CustomerError>? errors = null;
        Customer? value = null;
        var successes = 0;
        foreach (var step in steps)
        {
            var r = step.AsResult();
            if (r.TryGetValue(out var v)) { value = v; successes++; }
            else if (r.TryGetError(out var e)) (errors ??= []).Add(e);
        }
        if (errors is not null) return Validation.Fail<Customer, CustomerError>(errors);
        return successes == 1 ? Validation.Ok<Customer, CustomerError>(value!)
            : throw new InvalidOperationException($"the steps yielded {successes} successes and no error; exactly one is needed");
    }

    public static CustomerResult YieldFirst(IEnumerable<CustomerResult> steps)
    {
        CustomerResult? last = null;
        foreach (var step in steps)
        {
            if (!step.AsResult().TryGetValue(out Customer? _)) return step;
            if (last is not null) throw new InvalidOperationException("the steps yielded a second success");
            last = step;
        }
        return last ?? throw new InvalidOperationException("the steps yielded nothing");
    }
}
```

The same code compiles and behaves the same on `net10.0` (closed hierarchy) and `net11.0` (nested union).

## Against what is already there

| Need | Today | Iterator |
|---|---|---|
| Gather errors from a loop of the same step | `TraverseAll` / `CollectAll` (24c) | `Yield`; no gain |
| Gather errors from a fixed set of same-typed checks | `Validation.Combine(params ReadOnlySpan<…>)` (24c) | `Yield`; no gain |
| Gather errors from steps of different types, branches and loops, then one success | `Combine` arity overloads (up to 8 values), nested `Combine`, or a hand-kept `List<Error>` | `Yield`; plain control flow, leaves need no wrapping |
| Fail fast through dependent steps | `Failed(out value, out error)` + `return error;` (24b) | `YieldFirst`; longer: `{ yield return x; yield break; }` |
| Fail fast with `finally`/`using` | `Failed`; ordinary `return` runs them | `YieldFirst`; disposing the iterator runs them |
| Fail fast with `await` | `Failed` in an `async` method; the `await` short-circuit was not shipped (24e) | an async iterator and `YieldFirstAsync` (not probed) |

So `Yield` earns its place where the steps are heterogeneous and the control flow is irregular. `YieldFirst` is
never shorter than `Failed`, which stays the fail-fast style; its case is that one iterator can be read either way,
and an async form.

## Costs

- **The compiler doesn't know the driver stops.** After `yield return error` the iterator body goes on unless it
  says `yield break`, so a dependent step needs both. Under `Yield` the body goes on by design.
- **Lazy.** Nothing runs until a driver enumerates; an iterator passed nowhere does nothing, and enumerating twice
  repeats every side effect. This wants an analyzer, as RF0001 is for a dropped result (next free id: RF0012).
- **Counting successes is a run-time rule.** Zero successes and no error, or two successes, throw
  `InvalidOperationException`; no analyzer can count `yield return`s through branches.
- **A shape whose `Success` is an interface** has no conversion from the value (C# forbids one from an interface), so
  its success has to be yielded as `Shape.Ok(value)`.
- **Element type is one shape.** A step of another type (`Result<Order>`) has to be converted or inspected before it
  can be yielded; its success value can't be yielded.
- **Allocation.** One iterator object per call, and `Yield` a list on failure. Not measured.

## Prototype (stack B 26b)

Both forms are built: the core has `Result.Yield`/`YieldFirst` over `IEnumerable<Result<T, TError>>` and over any
`IResultShape<T, TError>`, and the generator writes `CustomerResult.Yield`/`YieldFirst` as one-line forwarders to the
shape overloads. Read side by side in `tests/Resultful.Shop.Tests/StepsTests.cs`:

| Form | Call | Iterator body |
|---|---|---|
| generated | `CustomerResult.Yield(Owner(…))` | yields the success and any leaf, inner ones too |
| generic, shape | `Result.Yield<CustomerResult, Customer, CustomerError>(Owner(…))`: C# can't infer `T` and `TError` from the constraint | same as generated |
| generic, core result | `Result.Yield(OwnerCore(…))` | on `net11.0` no leaf converts, not even a direct one (CS0029); every step needs `.AsResult()`. On `net10.0` leaves convert by inheritance |

Found on the way:
- On a `net11.0` union shape, `TryGetValue(out _)` is ambiguous (CS0121: one overload per leaf); iterators have to
  write `TryGetValue(out Customer? _)`.
- The generated pair costs 2 members per shape without `ManyErrors`; the budgets still hold. Neither has a loop,
  so the IL budget is unchanged.
- The rules hold on both TFMs: errors in order, the one success, disposal on the first error, zero or two successes
  and a zero value step throw, a second read repeats the steps (CsCheck laws against the steps read one by one).

Recommendation: keep both, as built. Generated forwarders are what users call; the core-result overloads serve code
without shapes; the shape overloads are the shared implementation. Not built yet: RF0012, `ManyErrors` shapes, a
rule for shapes without `Success`, async drivers.

## Not settled by the probe

- `ManyErrors` shapes: whether `Yield` returns the shape itself and takes every error of a failed step.
- Shapes with no `Success` (`Unit`): whether zero successes is then a success, so no trailing `yield return Unit`.
- A step that is the zero value: decision 10 says it throws; the probe's `TryGetValue` does, but it isn't tested.
