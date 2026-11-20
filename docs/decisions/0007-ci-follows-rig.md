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

Publishing is manual: running the workflow on `main` with the `publish` input ticked pushes the packed packages,
using the org's `NUGET_KEY` secret in the `release` environment. Merging never publishes, so pull requests can
land freely while the library is being reshaped. (The previous workflow tried to publish on every push to `main`,
but its condition read a step-level `env` from the step's own `if`, so it never fired.)

## Previews

0.3.0 is previewed before it ships. A preview is published only by hand: the maintainer dispatches the workflow
from a `rtkelly13/stack-*` branch with a preview number. Agents never dispatch it and never push to NuGet; they run
its dry run in CI and hand over the command.

```bash
gh workflow run ci.yml --ref rtkelly13/stack-NN-name -f preview=N -f dry-run=true    # pack and check only
gh workflow run ci.yml --ref rtkelly13/stack-NN-name -f preview=N -f dry-run=false   # publish 0.3.0-preview.N
```

- `publish-preview` checks the branch (a `rtkelly13/stack-*` branch other than `rtkelly13/stack-plan`) and the
  number, packs every project under `src/` as `0.3.0-preview.N`, fails unless every package has that version,
  restores the set together into an empty packages folder, and runs the package check (`eng/preview-packages.cs`,
  then `eng/verify-package.cs`). It has no secrets.
- `publish-preview-push` runs only when `dry-run` is false and `build` and `lint` pass. It refuses any package that
  isn't `0.3.0-preview.N`, then pushes with the `NUGET_PREVIEW_KEY` secret of the `nuget-preview` environment.
- `preview-dry-run` runs the same pack and checks as `0.3.0-preview.0` on every pull request, so a branch is known to
  pack before anyone dispatches it.
- Every package gets the same preview version, an exception to
  [decision 11](0011-package-family.md)'s per-package versions until stable 0.3.0.
- Preview numbers are sequential. A preview is never deleted, only unlisted. A number is used once (the push has no
  `--skip-duplicate`), so a failed or bad preview is followed by the next number, never re-pushed.
- The stable `publish` job is unchanged and still runs only on `main`.

**The security boundary is the `nuget-preview` environment, not the workflow.** The branch checks in `ci.yml` catch
mistakes, such as dispatching from the wrong branch. They don't stop an attack: anyone who can push a branch can
push a `rtkelly13/stack-*` branch with a changed `ci.yml` that names the environment. So the environment must have:

- a deployment branch rule that allows only `rtkelly13/stack-*`, and
- the maintainer as a required reviewer, so every push to NuGet waits for his approval. This is required, not
  optional.

`publish-preview-push` has its own concurrency group, `publish-preview`, shared by every branch, with
`cancel-in-progress: false`: a push to a branch, or another dispatch, never cancels a NuGet push midway. GitHub
keeps at most one pending run per group, so if a third dispatch queues while one push runs and a second waits, the
second (pending) one is cancelled. Check that each preview you dispatched actually ran.
