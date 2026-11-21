---
type: decision
title: "default is not a success"
description: "The zero value of every result type is made invalid at run time, new Result<T>() doesn't compile, and an analyzer catches the rest."
status: stable
tags: [resultful, decision, correctness]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 10. `default` is not a success

C# can't stop a struct's zero value from existing: `default`, array elements, uninitialised fields and `new T()`
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

Built in 0.3 (stack PR 08b): `IsSuccess` and `IsFailure` are both `false` on the zero value, and there is no
separate `IsDefault`. Only reading the value, the error or the case throws; `ToString` (`"<default Result>"`), `Equals`,
`GetHashCode` and the debugger display never throw, so logging, dictionaries and the debugger survive the bug. A
failure always holds an error: `Fail(null)` throws `ArgumentNullException`, and on `net11.0` `Fail` rejects an error
union with no case ([decision 17](0017-error-unions.md)), whether the type argument is the union, a `Nullable` of it,
an interface or `object`. The list builders reject a `null` list too (0.2 read it as no errors). A success may hold
`null`. The parameterless constructor throws, so `Activator.CreateInstance`, `new T()` under a `new()` constraint and
any serializer that constructs through it now throw: the JSON converters (12b) must build results with `Ok` and
`Fail`, never through that constructor.

Consequences: this is a behaviour break and ships with `0.3.0-beta01` ([decision 8](0008-version-0-3-0.md)), with
characterisation tests from roadmap M1 showing the change.
