---
type: decision
title: "A family of satellite packages around a dependency-free core"
description: "Integrations and analyzers ship as separate packages with explicit dependency rules; all packages share one version and are released together (amended from per-package versions)."
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
  version only moves when the core does. Both repositories release the same way. (Amended: releases are
  lockstep instead; see "How a release works".)

Still open: the exact set of satellites. The maintainer accepted the recommended family but wasn't sure of it,
so each satellite is confirmed when its milestone starts (M5), against the [vision](../vision.md). The
validation package must stay an adapter (plumbing, not rules), and diagnostics must stay opt-in hooks that log
nothing by default.

Consequences:
- The package check runs on every package and allows a package with no dependency groups (the core, after M3).
- Releases are lockstep: every package shares one version and one tag. This replaces the per-package versions,
  change detection and per-package tags first planned here (amended; see "How a release works" below).
- The compatibility question, "which `Resultful.AspNetCore` goes with which `Resultful`?", is answered by each
  satellite's dependency range, and the README lists the minimum core version per satellite.

## How a release works

Amended on 21 November 2026: releases are lockstep, as `AGENTS.md` records. The package family and its dependency
rules above stand; only the per-package versioning is replaced.

- **One version for every package**, `VersionPrefix` in `src/Directory.Build.props`. A release starts with a pull
  request that bumps it, so every release is reviewed.
- **Publishing is a manual workflow run on `main`** with `publish` ticked ([decision 7](0007-ci-hardening.md)).
  It needs the `build`, `lint` and both compatibility jobs, pushes every packed package, and then tags the run's
  commit `v<version>`, for example `v0.2.0`. If that tag already points at another commit, the job fails.
- **If a push or the tag fails part way**, use **Re-run failed jobs** on the same run. `--skip-duplicate` skips the
  packages already pushed, and the tag step accepts a tag that already points at the run's commit.
