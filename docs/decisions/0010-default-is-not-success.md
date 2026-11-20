---
type: decision
title: "default is not a success"
description: "With the dependency-free storage, default(Result<T>), default(VoidResult) and default(Option<T>) stop meaning success."
status: proposed
tags: [resultful, decision, correctness]
decided:
  by: ""
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 10. `default` is not a success

Today `default(Result<int>)` is `Ok(0)`, `default(VoidResult)` is `Ok`, an element of `new Result<string>[1]` is
`Ok(null)`, and `default(Option<int>)` is `Some(0)`. All four were verified on 0.2.0-beta06. The cause is the OneOf
storage, whose case 0 is the success case.

With the dependency-free storage ([decision 1](0001-core-is-dependency-free.md), roadmap step 5), the zero value
becomes an explicit *uninitialised* state:

- `Match`, `Switch`, `Value` and the operators throw `InvalidOperationException` on it.
- `IsDefault` reports it.
- An analyzer flags `default(Result<T>)`.

For `Option<T>`, `default` becomes `None`, which is a safe meaning.

Why: an uninitialised field, an array slot or a missing JSON value must never pass as a success. Failure was
considered as the zero value, but a failure needs an error value that `default` can't supply, so it was rejected.

Consequences: this is a behaviour break and ships with `0.3.0-beta01` ([decision 8](0008-version-0-3-0.md)), with
characterisation tests from roadmap step 4 showing the change.
