---
type: decision
title: "Targets: netstandard2.0, net10.0 and net11.0"
description: "Three targets; union-specific behaviour only on net11.0."
status: stable
tags: [resultful, decision]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 2. Targets: `netstandard2.0`, `net10.0` and `net11.0`

The library targets `netstandard2.0` (for libraries and .NET Framework consumers), `net10.0` (the current LTS)
and `net11.0`. Behaviour that depends on C# unions is behind `#if NET11_0_OR_GREATER`; the public API is the same
on every target apart from that. The SDK is pinned to 11.0.100 in `global.json`.

`net8.0` and `net9.0` consumers resolve to `netstandard2.0`. `IsAotCompatible`/`IsTrimmable` are set on the
net8+ targets.
