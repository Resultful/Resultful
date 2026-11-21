---
okf_version: 0.2
type: guide
title: "Comparing Resultful"
description: "Honest comparison of Resultful against exceptions, OneOf, ErrorOr, FluentResults, and LanguageExt."
status: stable
tags: [resultful, guide, comparison]
generated:
  by: agent:antigravity
  at: 2026-11-21
---

# Comparing Resultful

| Capability | Plain Exceptions | `OneOf` | `ErrorOr` / `FluentResults` | `LanguageExt` | **Resultful** |
|---|---|---|---|---|---|
| Core NuGet dependencies | 0 | 0 | 0 | Many | **0** |
| Typed closed error hierarchies | No | Generic arity (`OneOf<T0..T8>`) | Flat `Error` list | Yes (FP-heavy) | **`[ClosedHierarchy]` + `.NET 11` unions** |
| Zero-alloc generated result shapes | N/A | Struct union | Heap list (`ErrorOr` / `FluentResults`) | Mixed | **Yes (`[ResultShape]`, 0 B)** |
| Compile-time unobserved / `default` guards | No | No | No | Partial | **Yes (`RF0001`–`RF0010`, `CS8509`)** |
| PII-safe HTTP / GraphQL / OTel adapters | Manual | Manual | Manual | Manual | **First-class lockstep packages** |
| Native AOT & `System.Text.Json` source gen | Yes | Manual | Reflection-heavy | Partial | **First-class AOT converters** |

## When NOT to use Resultful

- **Truly exceptional faults** (out-of-memory, corrupted state, programmer invariant bugs): throw an exception and let the boundary turn it into a 500. Resultful is for *expected, modelled* domain and validation failures.
- **Tiny scripts or single-file tools:** standard exceptions or nullable return values are simpler.
- **Full Haskell/monad-transformer style FP in C#:** `LanguageExt` is built for that; Resultful stays unapologetically idiomatic C#.

## Adopting incrementally

If you already use `OneOf`, `Resultful.OneOf` bridges `OneOf<T, TError>` and `Result<T, TError>` without forcing a big-bang rewrite.
