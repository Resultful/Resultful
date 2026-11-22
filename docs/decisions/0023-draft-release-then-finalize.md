---
type: decision
title: "A draft GitHub Release records what was staged; finalize.yml publishes it"
description: "release.yml drafts a GitHub Release per package before staging it; once the version is promoted on nuget.org, finalize.yml checks it and publishes the draft, which creates the tag. Amends decision 22."
status: stable
tags: [resultful, decision, release, ci]
decided:
  by: human:rtkelly13
  at: 2026-11-22
generated:
  by: claude-opus-5.5
  at: 2026-11-22
---

# 23. A draft GitHub Release records what was staged; finalize.yml publishes it

This amends [decision 22](0022-releases-are-a-dispatch-map.md). It replaces its `finalize` job and the
`release-verify` environment.

Between staging and promotion, a release is pending, and something has to record it: which package and version,
staged from which commit, with which notes. **That record is a draft GitHub Release.**

- **`release.yml`'s `draft` job** runs after `pack` and before `publish`. For each package it creates a draft release
  named for the tag `<package>/v<version>`, targeting the run's commit, with the release notes. A draft creates no
  tag. If the tag already has a release, draft or published, the job fails and nothing is staged.
- **`pack`** checks that each package's nuspec records the run's commit (`<repository commit=…>`), the value
  `finalize.yml` later matches against the draft. 0.2.0-beta05 on nuget.org doesn't record it.
- **`publish`** stages the packages only after every draft exists, so each staged version has a draft.
- **`finalize.yml`** is the last step, run after the version is promoted on nuget.org, by hand or by a local agent:
  `gh workflow run finalize.yml -f tag=Resultful/v0.2.0`. It checks:
  - there is exactly one draft for the tag, and no tag;
  - the version is on nuget.org;
  - the package's repository commit is the draft's commit;
  - every package it depends on is already finalized.

  It then publishes the draft, and GitHub creates the tag at the draft's commit, even if `main` has moved on since. It
  reads the tag back to confirm. If the version isn't public yet it changes nothing, so it is safe to run again.
- **`discard: true`** deletes a draft whose version is not on nuget.org: it expired there, or was rejected.

| Job | Does | Permissions |
|---|---|---|
| `release.yml` `draft` | Creates the draft release for each package; fails if the tag already has one. No checkout | `contents: write` |
| `finalize.yml` `finalize` | Checks the version is public and built from the draft's commit, then publishes the draft. No checkout | `contents: write` |

Why:
- A tag would be the obvious record, but tags spread: `git fetch` brings them along, deleting one doesn't remove it
  from clones, forks and mirrors, and tools that read tags (Renovate, Dependabot, proxies) treat it as a release. A
  branch is worse. A draft is not a git ref, only people who can write to the repository can see it, and deleting it
  leaves nothing behind.
- release-drafter works the same way, and release-please (labels) and semantic-release (git notes) also keep pending
  state out of tags.
- An approval that waited on a paused run, `release-verify`, had to be approved within 30 days, couldn't be repeated,
  and tied the tag to the run. Promotion on nuget.org is already the human, 2FA step. Automation may stage as much as
  it likes, but nothing goes public until a person promotes it, so finalizing needs no second approval.

Consequences:
- `plan` runs with `contents: read`, which cannot see drafts, so the `draft` job does that check. An existing tag means
  the version is released, and `plan` refuses it. So does a version that is on nuget.org without its tag: finalize its
  draft.
- "Re-run all jobs" stops at `draft` once a draft exists, so a version is never staged twice by accident.
  "Re-run failed jobs" after a failed `publish` stages again, as before; check nuget.org's staged packages first.
- A staged version that is never promoted leaves its draft behind. Nothing deletes it on a timer: run
  `finalize.yml` with `discard`.
- GitHub accepts a second draft with the same tag without complaint, and ignores `target_commitish` if the tag
  already exists. Both scripts check for these, and `finalize` refuses rather than guessing.
- A dependant promoted before its dependency waits: `finalize` refuses it until the dependency's draft is published.
- If nuget.org or the GitHub API answers with anything but a clear yes or no, `finalize` stops rather than guessing,
  so an outage can't make it discard a live version's draft.
- Dry runs stop after `pack`, so they never exercise `draft`, `publish` or `finalize`.
- The `release-verify` environment is deleted once the last release that used it is tagged.
