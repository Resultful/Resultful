---
type: log
title: "Log"
description: "What changed in the Resultful documents, by date."
status: stable
tags: [resultful, log]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Log

## 2026-11-20

- Layout moved (#33): `src/Resultful`, `tests/Resultful.Tests`, `samples/Resultful.Examples`, `Resultful.slnx`.
  Package settings are in `src/Directory.Build.props`; test and sample settings in their folders' props files,
  replacing the project-name conditions. No code changed.

- Decisions 9, 11 and 13 accepted. Operators are opt-in through `Resultful.Operators`. Packages are versioned on
  their own and only what changed is published, as in `Resultful.Assertions`. `Resultful.Analyzers` is a separate
  package. The satellite set is accepted as recommended and confirmed per milestone. The layout move is the first
  M1 pull request. Decision 10's front matter repaired.

- Decision 12 accepted: `Resultful.Assertions` holds many assertion projects and publishes only what changed.
  The rename, its vision and the selective publishing were handed to a session in that repository.
- Long-term plan: roadmap rewritten as milestones (M1 to M7, beyond 1.0, and an assertions track); architecture
  added; decisions 11 to 13 proposed (package family, `Resultful.Assertions`, repository layout). The
  exploration gains the collections probe.
- Decision 10 accepted: an invalid zero value, an obsolete-error parameterless constructor, and an analyzer rule.
- Exploration *Seamless with .NET*: C# 14 extension operators probed against the current types; two
  existing hazards found (`default` is success; a bare string converts to an error). Decisions 9 and 10 proposed.
- Roadmap step 3 done (#24): SDK 11, `net11.0`, public API tracking.
- Documents started as an OKF bundle, following the rtkelly-labs projects: vision, roadmap, decisions, log.
- Vision rewritten around the project's intention rather than its history.
- Roadmap restructured as a series of pull requests to a stable release; decision 8 updated to match.
- Decision 7 updated: ls-lint and Dependabot added, docs-only gate left out, publishing is a manual workflow run.
- Decisions 1 to 8: a dependency-free core with `Resultful.OneOf`; three targets with net11 union behaviour
  behind `#if`; unions chosen by experiment; additive sequential composition; package shape checked in CI;
  a mixed test stack; hardened CI; `0.3.0-beta01`.
