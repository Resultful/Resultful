---
type: decision
title: "Resultful.FSharp is thin interop with FSharp.Core"
description: "Resultful.FSharp provides two-way conversions between Resultful core types and FSharp.Core without duplicating FsToolkit. Extends decision 11."
status: stable
tags: [resultful, decision, packages, fsharp]
decided:
  by: human:rtkelly13
  at: 2026-11-21
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# 19. `Resultful.FSharp` is thin interop with `FSharp.Core`

This extends [decision 11](0011-package-family.md). Its rules apply unchanged: the core references nothing, and a
satellite targets what its dependency targets.

F# code can take Resultful values from C# libraries and hand them back, and does everything else with F#'s own
tools (`FSharp.Core`, FsToolkit.ErrorHandling, its own assertion libraries).

- Conversions both ways between each core type and its `FSharp.Core` counterpart: results with `Result<'T, 'E>`,
  `Option<T>` with `'T option` and `'T voption`, `Validation` with `Result<'T, 'E list>`. Converting a zero value
  throws, as the core does.
- Written in C#, referencing `FSharp.Core` at the lowest version that works, so the repository's C# tooling
  (public API tracking, XML docs, package validation) applies. Extension methods read naturally from F#
  (`r.ToFSharp()`, `fsResult.ToResultful()`).
- No computation expressions, curried modules, active patterns or assertions: FsToolkit and F#'s assertion
  libraries already cover them once a value is converted. A small F# test project proves the conversions read
  well from F#.

Why:
- F# already has `Result`, `option` and a mature ecosystem around them; a team mixing F# and C# needs only the two
  to meet without hand-written glue. The strongest case for this project is C#, so the F# package stays small.
