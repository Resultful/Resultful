---
type: decision
title: "CI follows rig's hardening, not its whole job graph"
description: "Least-privilege tokens, zizmor, ls-lint, Dependabot, pack checks on every PR; the required check stays build; main publishes."
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
`persist-credentials: false`, `setup-dotnet` from `global.json`, zizmor, ls-lint (file and folder naming,
and no shell or PowerShell scripts), Dependabot with a cooldown, a NuGet cache, and scripts as C# file-based apps rather than shell (rig's ls-lint bans `.sh`). Pack and the package check
run on every pull request; publishing to NuGet runs only on `main`.

A docs-only change gate is left out: the build is a couple of minutes, and a skipped required check
needs extra plumbing to stay safe.

The check `main` requires is named `build`, so the job that gates merges keeps that name.

Publishing uses the org's `NUGET_KEY` secret in the `release` environment. The previous workflow's condition
read a step-level `env` from the step's own `if`, so it never published. Merging to `main` now publishes, so a pull request that changes the version is only merged when the release is
intended.
