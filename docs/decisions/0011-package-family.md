---
type: decision
title: "A family of satellite packages around a dependency-free core"
description: "Integrations and analyzers ship as separate packages with explicit dependency rules; each package has its own version and is released on its own (decision 22)."
status: stable
tags: [resultful, decision, packages]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 11. A family of satellite packages around a dependency-free core

Resultful is the core plus separate packages:
- `Resultful.Analyzers`
- `Resultful.OneOf`
- `Resultful.AspNetCore`
- `Resultful.Text.Json`
- `Resultful.FluentValidation`
- `Resultful.Diagnostics`

[The architecture](../architecture.md) gives each package's allowed dependencies and targets. The rules:

- The core references nothing.
- A feature that needs only in-box APIs on newer targets goes in the core behind `#if`.
- A satellite targets what its dependency targets, and drops `netstandard2.0` rather than add polyfills.
- **The analyzers are their own package**, not packed into the core. A project opts in by referencing
  `Resultful.Analyzers`, and the core package carries no analyzer assets.
- Every package passes the package check and tracks its public API.
- **Each package has its own version, and a release publishes only the packages that changed**, as in
  `Resultful.Assertions` ([decision 12](0012-assertions-live-in-resultful-assertions.md)). A satellite depends
  on the lowest core version it needs. Publishing stays a manual workflow run.

Why:
- Each framework boundary (HTTP, JSON, validation, logging) is where result types are most awkward, and each
  needs a dependency the core must not take.
- A separate analyzer package keeps the core package to one assembly, and lets the analyzers release new rules
  without a core release.
- Per-package versions mean a fix to one satellite doesn't republish packages that didn't change, and the core's
  version only moves when the core does. Both repositories release the same way. (Lockstep was tried briefly
  and dropped; see "How a release works".)

Still open: the exact set of satellites. The maintainer accepted the recommended family but wasn't sure of it,
so each satellite is confirmed when its milestone starts (M5), against the [vision](../vision.md). The
validation package must stay an adapter (plumbing, not rules), and diagnostics must stay opt-in hooks that log
nothing by default.

Consequences:
- The package check runs on every package and allows a package with no dependency groups (the core, after M3).
- Each package is released on its own, with its own tag (`<package>/v<version>`), by naming it in a release run.
- The compatibility question, "which `Resultful.AspNetCore` goes with which `Resultful`?", is answered by each
  satellite's dependency range, and the README lists the minimum core version per satellite.

## How a release works

Amended on 21 November 2026, twice. A lockstep model (one version in `src/Directory.Build.props`, one `v<version>`
tag, a version-bump pull request per release) replaced the per-package plan for a day.
[Decision 22](0022-releases-are-a-dispatch-map.md) replaces it: a manual `release.yml` run names the packages and
versions to release, nothing is committed, and each package gets its own tag and GitHub Release. The package family
and its dependency rules above stand.
