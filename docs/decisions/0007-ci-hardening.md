---
type: decision
title: "CI takes current hardening practice, not a full job graph"
description: "Least-privilege tokens, zizmor, ls-lint, Dependabot, pack checks on every PR; one required check, the ci gate; publishing is a separate manual workflow (decision 22)."
status: proposed
tags: [resultful, decision]
decided:
  by: ""
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 7. CI takes current hardening practice, not a full job graph

Proposed: take `permissions: {}` at the top with per-job `contents: read`,
`persist-credentials: false`, `setup-dotnet` from `global.json`, zizmor, ls-lint (file and folder naming,
and no shell or PowerShell scripts), Dependabot with a cooldown, a NuGet cache, and scripts as C# file-based apps rather than shell (ls-lint bans `.sh`). Pack and the package check
run on every pull request; publishing to NuGet runs only on `main`.

A docs-only change gate is left out: the build is a couple of minutes, and a skipped required check
needs extra plumbing to stay safe.

Job names are short snake_case. `main` requires one check, `ci`: a gate job that passes only if every other job
passed, so adding a job never means touching branch protection.

Publishing is a separate, manual workflow, `release.yml` ([decision 22](0022-releases-are-a-dispatch-map.md)): it
publishes only a commit whose `ci` check passed, and authenticates with NuGet trusted publishing, so no API key is
stored. Merging never publishes, so pull requests can land freely while the library is being reshaped. (The previous
workflow tried to publish on every push to `main`, but its condition read a step-level `env` from the step's own
`if`, so it never fired.)
