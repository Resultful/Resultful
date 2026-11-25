---
type: exploration
title: "await as a short-circuit"
description: "The probe of decision 22 item 6: await on a Resultful result as Rust's ? or Haskell's do, through method-level async builders. What compiles, what runs, what it costs, what the compiler and the RF rules say, and why it should not ship as it stands."
status: draft
tags: [resultful, exploration, language, async]
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# `await` as a short-circuit

> The probe behind [decision 22](../decisions/0022-native-functional-paths.md) item 6 (Path 5 of
> [Functional style with native C#](native-functional-csharp.md)). It ships nothing. The probe was a test project,
> `tests/Resultful.AwaitProbe.Tests`, kept out of the repository once the recommendation was accepted; its code stays
> at [PR #87's commit `fd7e597`](https://github.com/Resultful/Resultful/blob/fd7e597c14ebafe07ebeca0ae4e24a70f5963136/tests/Resultful.AwaitProbe.Tests), and each finding below names the test that pins it. Everything marked
> **verified** ran on SDK 11.0.100 against `net10.0` and `net11.0`, in Debug and Release.

## The question

Can this be plain C#?

```csharp
[AsyncMethodBuilder(typeof(ResultBuilder<>))]
async Result<Receipt> PlaceOrder(OrderDto dto)
{
    var order = await ParseOrder(dto);   // a failure ends the method, returning the failure
    var stock = await Reserve(order);
    var payment = await Charge(order, Total(order));
    return new Receipt(stock.Sku, stock.Reserved, payment.Id);
}
```

Yes, with limits. The probe has three parts ([`Probe.cs`](https://github.com/Resultful/Resultful/blob/fd7e597c14ebafe07ebeca0ae4e24a70f5963136/tests/Resultful.AwaitProbe.Tests/Probe.cs)):

- **An awaiter per result type** (`ResultAwaiter<T>`, `ResultAwaiter<T, TError>`), reached through an extension
  `GetAwaiter` imported per file. `IsCompleted` is `IsSuccess`. On a failure only a result builder acts; any other
  builder that schedules a continuation gets it run at once, and `GetResult` then throws
  `AwaitedFailureException`.
- **Sync builders for `async Result<T>`**: `ResultBuilder<T>` *abandons* the method on a failure (records it, never
  resumes the state machine); `UnwindingResultBuilder<T>` *unwinds* it (resumes so `GetResult` throws, then turns the
  exception back into the failure in `SetException`).
- **Async builders for `async ValueTask<TResult>`** (`AsyncResultBuilder`, `UnwindingAsyncResultBuilder`), where
  `TResult` is `Result<T>` or `Result<T, TError>`. They wrap `AsyncValueTaskMethodBuilder<TResult>`, so real awaits
  (`Task.Delay`, I/O) suspend as in any async method, and a failed result ends the method.

## What the compiler allows (verified)

| Form | Result | Pinned by |
|---|---|---|
| `[AsyncMethodBuilder(typeof(B<>))] async Result<T> M()` | Compiles. A method-level builder can target a type that isn't task-like. | every scenario test |
| `async Result<T> M()` with no attribute | CS1983: not a task-like type. | `The_compiler_reports_the_limits_of_a_method_level_builder` |
| `async Result<T, TError> M()` with any builder | CS1983. The return type must have 0 or 1 type parameters. | same |
| A builder nested in `Typed<TError>` to carry the error type | CS8940: the builder must be an unbound generic of arity one whose containing type is non-generic. Reported only at emit. | `A_builder_nested_in_a_generic_type_cannot_carry_the_error_type` |
| `async ValueTask<Result<T, TError>> M()` with a method-level builder | Compiles: `ValueTask<>` has arity one, and the builder's type argument is the whole `Result<T, TError>`. | `Typed_errors_go_through_ValueTask_…`, `Typed_errors_mix_with_real_awaits` |
| `return error;` in `async Result<T>` | CS0029: `return` takes `T`. A failure has to be awaited: `await Result.Fail<T>(error)`. | `The_compiler_reports_…` |
| `[ResultBuilder]` as the attribute | Not possible. The compiler only knows `AsyncMethodBuilderAttribute`, with the builder's type as its argument. A type-level `[AsyncMethodBuilder]` on `Result<T>` would drop the per-method attribute but makes `Result<T>` task-like everywhere (a core change; not probed). | |

So the closest workable forms are `async Result<T>` for `Error` failures and `async ValueTask<Result<T, TError>>` for
typed errors. A typed result can still be *awaited* inside an `async Result<T>` method when its error derives from
`Error` (`A_typed_result_with_an_error_derived_from_Error_…`).

## Correctness (verified)

| Finding | Pinned by |
|---|---|
| Success, and a failure at the first, middle or last step, give the same result as `Bind` and early return, and no later step runs. | `Every_style_returns_the_first_failure_and_runs_no_later_step` |
| CsCheck: for any list of steps, both sync builders and both async builders (with and without real suspensions) agree with the `Bind` chain; `await r` returned as is gives `r`. | [`Laws`](https://github.com/Resultful/Resultful/blob/fd7e597c14ebafe07ebeca0ae4e24a70f5963136/tests/Resultful.AwaitProbe.Tests/Laws.cs) |
| An exception in a step propagates from the call, unwrapped (sync), or faults the `ValueTask` (async). | `An_exception_in_a_step_propagates_…`, `An_exception_after_a_real_await_…` |
| Awaiting the zero value throws, as reading it does (decision 10). | `Awaiting_the_zero_value_throws_…` |
| Loops work: `total += await Check(item)` stops at the first failed item. | `Loops_stop_at_the_first_failed_item` |
| A sync `async Result<T>` method can't await real work: `NotSupportedException` at run time, not a compile error. A completed task is fine. | `A_sync_result_method_cannot_await_real_work_…` |
| An error the method can't hold (an `Error` awaited in a `Result<T, ShopError>` method) fails at run time, and only on the failure path. | `An_error_type_the_method_cannot_hold_…` |
| An `AsyncLocal` set inside a sync builder method leaks to the caller; an `async Task` restores it. The sync builders skip the execution-context save that `AsyncTaskMethodBuilder.Start` does. | `An_AsyncLocal_set_in_a_sync_result_method_leaks_…` |

### `try`/`finally` and `using`

The central finding. A state machine that is never resumed never runs its `finally` blocks, and `using` is a
`finally`. The builder can't run them for it: it can't see the method's locals, and the only way back into the
method is to resume it, where `GetResult` must either return a value (and the method carries on with garbage) or
throw.

| Builder | On a failure | Pinned by |
|---|---|---|
| Abandoning | `finally` and `using` after the failed await **don't run**. No error, no warning: a leaked connection or a held lock. | `Abandoning_skips_finally_and_using_after_the_failed_await` |
| Unwinding | They run, in order, and the method still returns the failure. | `Unwinding_runs_finally_and_using_in_order_…` |
| Unwinding | A `catch (Exception)` in the method catches the short-circuit, and the method returns whatever the catch returns: a success. `catch (Exception e) when (e is not AwaitedFailureException)` avoids it. | `Unwinding_is_caught_by_a_catch_all_…` |
| Abandoning, async | Inconsistent: a failure already in hand when awaited abandons (no dispose); a failure that arrives after a real wait reaches the method through `GetResult`, so it unwinds (dispose runs). | `Abandoning_async_returns_the_first_failure_but_disposes_only_…` |
| Unwinding, async | Always disposes. | `Unwinding_async_returns_the_first_failure_and_always_disposes` |

### Awaited outside a builder

The first idea, an awaiter that never resumes on a failure, leaves a plain `async Task` incomplete for ever, with
no exception and no log (`A_never_resuming_awaiter_leaves_a_plain_async_Task_incomplete_…`). Throwing from
`OnCompleted` instead would be worse: in the runtime's source, `AsyncTaskMethodBuilder` catches an exception from
the awaiter's `OnCompleted` and rethrows it on the thread pool, which ends the process (not run here).

The probe's awaiter resumes at once and throws from `GetResult`, so the task faults with `AwaitedFailureException`,
whose `Error` is the failure. Its message is fixed text; the error's contents never reach it (sentinel test). Awaited
inside a builder method, such a faulted task becomes the failure again. Pinned by
`A_result_awaited_in_a_plain_async_Task_faults_it_instead_of_hanging`,
`The_personal_data_sentinel_never_reaches_the_exception_text`,
`A_short_circuit_faulting_a_plain_task_becomes_a_failure_again_…`. The compiler says nothing about it
(`Awaiting_a_result_in_a_plain_async_method_compiles_silently`).

## Nullability (verified)

For a non-null `T` the value after `await` is non-null with no `!` and no warning; the test project builds with
`TreatWarningsAsErrors` (`Values_after_await_are_the_success_values_and_never_null`).

For a maybe-null `T` the compiler loses it. `await` on a `Result<string?>` gives a `string?`, but the nullable state
after the `await` is not-null, so `note.Length` compiles with no CS8602, even assigned to a declared `string?`. A
`Task<string?>` warns. The cause is the extension `GetAwaiter`: with the same awaiter, an instance `GetAwaiter`
keeps the state, while a classic extension method and a C# 14 extension member both lose it; calling
`GetAwaiter().GetResult()` directly keeps it. Pinned by `Nullable_flow_loses_a_maybe_null_T_…` and
`Only_an_instance_GetAwaiter_keeps_the_maybe_null_state`. So nullable-correct `await` needs `GetAwaiter` as an
instance member of the core types, which makes every result awaitable in every file.

## Allocations and time (verified)

The same three-step pipeline ([`CostTests`](https://github.com/Resultful/Resultful/blob/fd7e597c14ebafe07ebeca0ae4e24a70f5963136/tests/Resultful.AwaitProbe.Tests/CostTests.cs)), with cached errors so only the style allocates. Bytes per call, measured
with `GC.GetAllocatedBytesForCurrentThread`, `net10.0` and `net11.0` identical:

| Style | Release, success | Release, failure | Debug, success | Debug, failure |
|---|---:|---:|---:|---:|
| Early return (`TryGetValue`) | 0 | 0 | 0 | 0 |
| `Bind` chain (closure over one local) | 24 | 24 | 152 (136 on net11) | 152 (136) |
| `await`, abandoning | 0 | 0 | 88 | 120 |
| `await`, unwinding | 0 | 304 (the exception) | 88 | 424 |
| `await`, `ValueTask` builder, no real await | 0 | 0 | 88 | 120 |

Release figures need full optimisation: with tiered compilation the first thousands of calls run unoptimised code
that boxes the awaiter for the builder's interface check (32 B). The test project turns tiering off to measure the
steady state. Debug builds make every state machine a class (88 B) for Edit and Continue.

With a real suspension the custom async builder allocates what `AsyncValueTaskMethodBuilder` does, plus up to 16 B
because the boxed state machine also holds the result awaiter (`A_real_suspension_costs_the_same_…`).

Time per call, BenchmarkDotNet 0.15.2 `--job short`, .NET 10.0.12, Xeon 2.1 GHz (indicative: three iterations, wide
error bars):

| Style | Success | First step fails |
|---|---:|---:|
| Early return | 1.4 ns | 1.1 ns |
| `Bind` | 12.5 ns | 9.6 ns |
| `await`, abandoning | 44.8 ns | 32.1 ns |
| `await`, unwinding | 47.6 ns | 2,973 ns |
| `await`, `ValueTask` builder | 56.8 ns | 33.1 ns |

The state machine (a struct holding every awaiter and local, behind a `try`/`catch` that blocks inlining) costs
30–45 ns, allocation-free. Unwinding costs about 3 µs per failure: an exception thrown and caught.

## Analyzers and compiler warnings (verified)

[`DiagnosticTests`](https://github.com/Resultful/Resultful/blob/fd7e597c14ebafe07ebeca0ae4e24a70f5963136/tests/Resultful.AwaitProbe.Tests/DiagnosticTests.cs) compiles `Probe.cs` with consumer code in process and runs every RF rule:

- **No misfire on await-style code.** A typical handler reports nothing. In the probe itself the only report is
  RF0003 on the naive awaiter's unchecked `result.Value`, which is right.
- `await Validate(order);` (a dropped `Unit`) is not RF0001. A dropped call of an await-style method is RF0001, as
  for any result.
- **CS4014 joins RF0001** once results are awaitable: on a dropped call of an async (builder) method, and on any
  dropped result inside an async method. Its advice, "consider applying the await operator", is wrong in a plain
  async method, where awaiting a failure throws.
- **No CS1998** for an async method with no `await`: neither SDK 10.0.4xx nor 11.0.1xx reports it for any async
  method.
- The compiler never flags a result awaited in a plain async method, a real await in a sync builder method, or an
  error-type mismatch. Each would need a new RF rule.

## Stack traces (verified)

With `[StackTraceHidden]` on the sync builders, an exception thrown in a step shows the step, the state machine's
`MoveNext` (with its line) and the kickoff method, both under the method's own name. The `Bind` chain shows a frame
per lambda (`<PlaceOrderBind>b__18_0`), plus `Bind` and `Match` where the JIT doesn't inline them
(`Stack_traces_show_the_method_twice_and_no_builder_frames`):

```text
at Resultful.AwaitProbe.Tests.Shop.Charge(Order order, Int32 amount) in Domain.cs:line 49
at Resultful.AwaitProbe.Tests.Shop.PlaceOrderAwait(OrderDto dto) in Domain.cs:line 82
at Resultful.AwaitProbe.Tests.Shop.PlaceOrderAwait(OrderDto dto)
```

Not checked in a debugger: the builders and awaiters carry `[DebuggerNonUserCode]`, so Just My Code should step
over them; the method is an async state machine, so its locals are hoisted fields, and stepping over an `await` that
fails should leave the method without reaching the next line.

## Reproducing the numbers

```bash
git fetch origin fd7e597c14ebafe07ebeca0ae4e24a70f5963136   # PR #87's commit; GitHub keeps it
git checkout fd7e597c14ebafe07ebeca0ae4e24a70f5963136
dotnet test tests/Resultful.AwaitProbe.Tests -c Release -f net10.0 --logger "console;verbosity=detailed"
dotnet test tests/Resultful.AwaitProbe.Tests -c Release -f net11.0   # and -c Debug for the Debug column
```

The allocation table is the output of `CostTests.Bytes_per_call` (the project sets `TieredCompilation=false`).
The times came from a BenchmarkDotNet 0.15.2 console app, not committed, holding a copy of `Probe.cs` and the
`CostTests` pipelines as `[Benchmark]` methods with `[MemoryDiagnoser]` and `[Params(1, 11)]` for the start value,
run with `dotnet run -c Release -- --job short --filter '*'`.

## Recommendation: not shipped (decision 22 item 6 closed)

It works, and on the success path it is allocation-free. But each way of finishing it costs something decision 22
was meant to avoid:

1. **Cleanup.** Abandoning silently skips `finally` and `using`. Unwinding fixes that, at about 3 µs and 300 B per
   failure, and a `catch (Exception)` in the method turns the failure into a success.
2. **Nullability.** A maybe-null `T` loses its warning unless `GetAwaiter` is an instance member of the core types,
   which makes every result awaitable everywhere and brings CS4014 to every dropped result in async code.
3. **Typed errors.** `Result<T, TError>` can't be the return type; it needs `ValueTask<Result<T, TError>>`, so every
   caller handles a `ValueTask` (CA2012 flags reading its `Result` unchecked).
4. **Run-time-only mistakes.** A result awaited in a plain async method, a real await in a sync builder method and
   an error-type mismatch all compile, and fail only when a failure happens. The ValueTask builder also needs
   reflection to build a failure (no static abstract `Fail` in the core), so it isn't AOT-safe.
5. **Reading it.** It looks asynchronous and isn't, a failure is written `await Result.Fail<T>(e)` rather than
   `return e`, and each method needs `[AsyncMethodBuilder(typeof(…))]`.

`Failed(out value, out error)` (decision 22 item 1) gives the same early exit with ordinary control flow: zero cost,
`finally` and `using` intact, nullable flow correct, typed results as they are. Path 1 is the answer for
branching, loops, `try` and `using`; Path 5 is closed and stays an exploration.

### If it is reconsidered

A later decision would need all of these, and the probe's tests (at `fd7e597`) as the starting suite:

- Unwinding semantics only, plus an RF rule against a `catch` in a builder method that can catch
  `AwaitedFailureException`.
- An instance `GetAwaiter` on the core result types, accepting that every result becomes awaitable.
- RF rules for a result awaited outside a builder method, a real await in a sync builder method, and an awaited
  error type the method can't hold.
- A static abstract `Fail` on the result types (24c's `IResult<TSelf, T, TError>`), so the async builder needs no
  reflection.
- A separate opt-in package for the builders (`Resultful.Await`), so the core's surface doesn't grow.
