---
type: decision
title: "Sequential composition is additive: Then, Sequence and Check"
description: "Short-circuiting steps are added without changing existing operators."
status: stable
tags: [resultful, decision]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 4. Sequential composition is additive: `Then`, `Sequence` and `Check`

`Sequence(...)` starts a chain of `VoidResult` steps, `Then(...)` continues one (same-typed steps on
`Result<T>`, or a single step from `VoidResult` to a value), and `Check(...)` runs steps that may fail but keep
the value. Each step runs only if everything before it succeeded, and the first failure is returned unchanged with
any errors it already accumulated. Every form has an `Async` variant and a `Task` receiver, following the
library's convention: `Async` means the steps return a `Task`.

Why: `Plus`/`Fold`/`Reduce` accumulate, which is right for independent checks but wrong for dependent steps,
and chaining `Bind(_ => ...)` with an unused `Unit` is noisy. Nothing existing changes.
