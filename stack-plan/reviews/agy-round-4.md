# Round 4: Missing Pieces & Gaps Audit

## Critical (memory safety, runtime crash, or broken publish path)

1. **`Unsafe.As<TError, object?>(ref e)` in `union-probe-2` is memory-unsafe for custom `IUnion` structs.**
   - `union-probe-2/Program.cs:16-20` reinterprets any `IUnion` value type with `Unsafe.SizeOf<TError>() == IntPtr.Size` as `object?`. A user's custom `[Union]` struct holding a `long` or `(byte, int)` is 8 bytes on x64 with **no** GC reference — `Unsafe.As` produces a corrupt object reference / AV.
   - **Fix (PR `08b`):** Guard with `RuntimeHelpers.IsReferenceOrContainsReferences<TError>()` AND a cached check that `typeof(TError)` has exactly one instance field of type `object`, or dispatch via a constrained generic helper `where T : struct, IUnion` (`e.Value is null`, zero-alloc without `Unsafe.As`).

2. **Nested error union `IErrorUnion.Error` and `WithContext` throw at runtime as prototyped.**
   - `union-error-probe/Partial.cs:4` does `Value switch { Error e => e, _ => throw ... }`. For a nested union `union OrderError(OrderNotFound, PaymentError)` (Decision 17), `Value` is a boxed `PaymentError` (`IErrorUnion`), not an `Error`, so `.Error` throws `InvalidOperationException` and `WithContext` fails.
   - **Fix (PR `04c`, `13b`):** Emit `Value switch { Error e => e, IErrorUnion u => u.Error, _ => throw ... }` and dispatch `WithContext` per direct case (calling `leaf.WithContext` or `nested.WithContext` and re-wrapping). Add nested-union `.Error`/`.WithContext`/`.TryFind` `UERR-*` fixtures in `04c`.

3. **`04e` `publish-preview` `workflow_dispatch` inputs won't exist on `main` when dispatched from `rtkelly13/stack-*`.**
   - `GOAL.md:104`: `ci.yml` on `main` only defines `inputs.publish`; GitHub validates `workflow_dispatch` inputs against the default branch (`main`), so dispatching a new input or preview job from a stack branch can be rejected until `main` has it.
   - **Fix (PR `04` or `04e`):** Verify with a live dry-run `gh workflow run ci.yml --ref rtkelly13/stack-04e-publish-preview` in `04e`; if GitHub rejects branch-only inputs, place the minimal `workflow_dispatch` preview input in Stack A (`04`) so it lands on `main` with `0.2.0`.

## High (missing API assignments, broken consumer ergonomics, or wire-format gaps)

4. **`RF0008` (Error by default) on core 2-arg `Result.Match(v => ..., e => ...)` will break idiomatic C#.**
   - `DECISIONS-TAKEN.md:32` applies `RF0008` to core `Match` too, where `(onSuccess, onError)` can never reorder. Turning `result.Match(x => x + 1, e => 0)` into a compile error is pure friction.
   - **Fix (PR `16b`):** Scope `RF0008` strictly to generated multi-case `Match`/`Switch`/`TryMap` (ordered by hierarchy/FQN), never 2-arg core `Result.Match`/`Switch`.

5. **JSON deserialization contract is missing for error unions, unknown `type`, and colliding leaf simple names.**
   - `DECISIONS-TAKEN.md:21` specifies writing an error union as its leaf's simple name, but leaves **reading** undefined: (a) how `Resultful.Text.Json` deserializes `Result<T, OrderError>` (nested or flat union) under NativeAOT without reflection, (b) what happens when an unknown `type` arrives (rolling deploys: `MessageError` fallback vs `JsonException`), and (c) colliding simple names (`Orders.NotFound` vs `Payments.NotFound`).
   - **Fix (PR `04c`, `12b`):** Lock all three in `04c`'s wire-format spec and add `UERR-JSON-*` round-trip and unknown-type fixtures in `12b`.

6. **`IErrorUnion` has no owning PR in `GOAL.md`'s stack table, and its TFM scope is unstated.**
   - `07` (`attributes`) lists `IResultShape`/`IValidationShape` but omits `IErrorUnion`. Also unstated whether `IErrorUnion` ships on all TFMs (`netstandard2.0;net10.0;net11.0`) or `net11.0`-only.
   - **Fix (PR `04c`, `07`):** Assign `IErrorUnion` explicitly to `07` (shipping on all TFMs keeps `PublicAPI` uniform; reflect in `04c` `TargetApi`).

7. **Preview versioning (`0.3.0-preview.N`) vs per-package versioning (`04`) is unspecified.**
   - At checkpoint `17` (`0.3.0-preview.2`), if core didn't change since `19` (`0.3.0-preview.1`), `04e` must specify whether all packable packages stamp `0.3.0-preview.N` together or unchanged packages stay on `.1` with satellite nuspec floors pinned to `.1`.
   - **Fix (PR `04e`):** Stamp a uniform `0.3.0-preview.N` across all packable packages for preview runs and verify restore in `04e`'s dry run.

## Medium (real-world .NET consumer gaps)

8. **`ValidationFailed<TError>` and `Validation<T, TError>` lack structural equality.**
   - `06` adds structural `Equals`/`GetHashCode` for `Error.Context`, but `ValidationFailed<TError>(IReadOnlyList<TError> Errors)` inherits record reference-equality on `IReadOnlyList`, so two identical validation failures won't be equal in tests or dictionaries.
   - **Fix (PR `04c`, `06`, `10`):** Override `Equals`/`GetHashCode` element-by-element on `ValidationFailed<TError>` and `Validation<T, TError>`, with `VAL-EQ` fixtures.

9. **Non-default `Error.ToString()` PII/log format and `Fail(null!)` / empty-list validation guards aren't pinned.**
   - `08b` pins `default`'s `ToString()`, and `20a` hides `Message` in 500 HTTP bodies, but `Error.ToString()` in logs (record `PrintMembers` dumping typed PII properties vs `Message` + `Context` + `Cause`) and `Fail(null!)` / `Validation.Fail([])` argument exceptions are unpinned.
   - **Fix (PR `04c`, `06`, `08b`, `10`):** Decide `Error.ToString()` in `06` (`ERR-TOSTRING`) and add `ZERO-NULL-ERR` / `VAL-NONEMPTY` fixtures in `04c`.
