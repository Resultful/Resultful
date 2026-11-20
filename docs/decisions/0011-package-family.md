---
type: decision
title: "A family of satellite packages around a dependency-free core"
description: "Integrations ship as separate packages with explicit dependency rules, all versioned and released together."
status: proposed
tags: [resultful, decision, packages]
decided:
  by: ""
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 11. A family of satellite packages around a dependency-free core

Resultful is the core plus satellites:
- `Resultful.OneOf`
- `Resultful.AspNetCore`
- `Resultful.Text.Json`
- `Resultful.FluentValidation`
- `Resultful.Diagnostics`

Analyzers are packed into the core. [The architecture](../architecture.md) gives each package's allowed
dependencies and targets. The rules:

- The core references nothing.
- A feature that needs only in-box APIs on newer targets goes in the core behind `#if`.
- A satellite targets what its dependency targets, and drops `netstandard2.0` rather than add polyfills.
- Every package passes the package check and tracks its public API.
- **All packages in this repository share one version and release together** (lockstep).

Why:
- Each framework boundary (HTTP, JSON, validation, logging) is where result types are most awkward, and each
  needs a dependency the core must not take.
- Lockstep versioning means the question "which `Resultful.AspNetCore` goes with which `Resultful`?" never has to
  be asked. It costs a release of every package for any change, which is cheap from one workflow.

Checked against the [vision](../vision.md): the validation package is an adapter (plumbing, not rules), and
diagnostics is opt-in hooks that log nothing by default. Neither makes Resultful a validation or logging
framework.
