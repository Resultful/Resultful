---
type: decision
title: "default is not a success"
description: "C# can't stop a struct's zero value from existing: `default`, array elements, uninitialised fields and `new T()`
under a `new()` constraint all produce it. This was verified on .NET 11. So the zero value is made harmless and
the ways of writing it are caught, in three layers. All three ship with the dependency-free storage
([decision 1](0001-core-is-dependency-free.md), roadmap M3):

1. **The zero value is invalid at run time.** The storage keeps a state byte, where 0 means uninitialised and the
   other values mean success and failure. On an uninitialised value, `Match`, `Switch`, `Value` and the operators
   throw `InvalidOperationException`, and `IsDefault` reports it. This costs nothing, because the byte replaces
   OneOf's case index. For `Option<T>`, the zero value is `None`, which is a safe meaning, so it doesn't throw.
2. **`new Result<T>()` doesn't compile.** Each result type declares a parameterless constructor marked
   `[Obsolete(..., error: true)]` that throws, so the call is a compile error (CS0619). It does not block
   `default`.
3. **An analyzer covers the rest.** It flags `default(Result<T>)`, a `default` literal where a result is
   expected, and result fields that a constructor never assigns. It is one of the first rules in the analyzer
   package.

Why: an uninitialised field, an array slot or a missing JSON value must never pass as a success.

Alternatives rejected:
- **Failure as the zero value.** A failure needs an error that `default` can't supply.
- **Classes instead of structs.** `null` would then be the zero value and nullable warnings would catch it, but
  every result would allocate.

Still to check: the .NET 11 union experiment ([decision 3](0003-unions-are-chosen-by-measurement.md)) looks at
whether a union's zero value (`HasValue == false`) gets extra compile-time checks in an exhaustive `switch`.

Consequences: this is a behaviour break and ships with `0.3.0-beta01` ([decision 8](0008-version-0-3-0.md)), with
characterisation tests from roadmap M1 showing the change.
