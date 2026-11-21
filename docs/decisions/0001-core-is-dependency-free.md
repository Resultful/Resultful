---
type: decision
title: "The core package is dependency-free; OneOf interop moves to Resultful.OneOf"
description: "Resultful stops depending on OneOf; a separate package carries the interop."
status: stable
tags: [resultful, decision]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 1. The core package is dependency-free; OneOf interop moves to `Resultful.OneOf`

`Resultful` keeps its own discriminated storage inside `Result`, `VoidResult` and `Option`, and its own
`Resultful.None`. A new package, `Resultful.OneOf`, carries `ToOneOf()` (including the `Task` forms),
`FromOneOf` and conversions between `OneOf.Types.None` and `Resultful.None`.

Why: OneOf's old `netstandard1.3` group pulled vulnerable packages into net8+ apps (beta05 broke `paket install`
in a consuming project), and C# unions on .NET 11 will change how a union library fits at all. A result library should not make its
consumers audit someone else's graph.

This is a breaking change: `OneOf.Types.None` in `Option`'s public API becomes `Resultful.None`, and `ToOneOf`
needs the extra package. It is documented in the release notes with the migration.
