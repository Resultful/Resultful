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
  a mixed test stack; rig-style CI; `0.3.0-beta01`.
