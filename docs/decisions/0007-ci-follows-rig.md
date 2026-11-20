---
type: decision
title: "CI follows rig's hardening, not its whole job graph"
description: "Least-privilege tokens, a docs-only gate, zizmor, a target matrix; the required check stays build; main publishes."
status: proposed
tags: [resultful, decision]
decided:
  by: ""
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 7. CI follows rig's hardening, not its whole job graph

Proposed: from rig's CI, take `permissions: {}` at the top with per-job `contents: read`,
`persist-credentials: false`, `setup-dotnet` from `global.json`, a docs-only change gate, zizmor, a NuGet
cache, and scripts as C# file-based apps rather than shell (rig's ls-lint bans `.sh`). Pack and the package check
run on every pull request; publishing to NuGet runs only on `main`.

The check `main` requires is named `build`, so the job that gates merges keeps that name.

Merging to `main` publishes, so a pull request that changes the version is only merged when the release is
intended.
