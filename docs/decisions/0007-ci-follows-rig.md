---
type: decision
title: "CI follows rig's hardening, not its whole job graph"
description: "Least-privilege tokens, zizmor, ls-lint, Dependabot, pack checks on every PR; one required check, the ci gate; main publishes."
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

Job names are short snake_case. `main` requires one check, `ci`: a gate job that passes only if every other job
passed, so adding a job never means touching branch protection.

Publishing is manual: running the workflow on `main` with the `publish` input ticked pushes the packed packages
and tags the commit `v<version>`. It authenticates with NuGet trusted publishing, so no API key is stored: nuget.org
trusts this repository's `ci.yml` in the `release` environment and gives a key that lasts an hour
([decision 11](0011-package-family.md), "How a release works"). Merging never publishes, so pull requests can
land freely while the library is being reshaped. (The previous workflow tried to publish on every push to `main`,
but its condition read a step-level `env` from the step's own `if`, so it never fired.)
