---
type: decision
title: "Releases are a manual run with a map of packages and versions"
description: "release.yml takes Package=version pairs at dispatch time; no version is committed, each package is tagged <package>/v<version> and gets its own GitHub Release. Replaces lockstep releases (decision 11)."
status: stable
tags: [resultful, decision, release, ci]
decided:
  by: human:rtkelly13
  at: 2026-11-21
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# 22. Releases are a manual run with a map of packages and versions

This replaces the lockstep release in [decision 11](0011-package-family.md) ("How a release works") and the
publish job in [decision 7](0007-ci-hardening.md).

**A release is a manual run of `.github/workflows/release.yml`**, given the packages to release and their versions,
for example `Resultful=0.2.0-beta07` or `{"Resultful":"0.2.0-beta07"}`. A run releases only the packages it names,
so each package moves at its own pace.

- **No version is committed.** Source builds are `0.0.0-dev`. The release run writes a gitignored
  `release-versions.props` that sets each named package's `VersionPrefix`/`VersionSuffix`, and its
  `PackageValidationBaselineVersion`: the newest stable version on nuget.org on the same major (same major.minor
  while below 1.0). Per-project versions keep each `ProjectReference` dependency at its sibling's real version,
  which a global `-p:Version` would not.
- **Every package is listed in `.github/release-packages.json`**, with its package id, the files whose changes
  go in its release notes, and its `depends_on` and `coupled_with` packages.
- **Dry run is the default.** It plans, checks and packs on any branch and publishes nothing.

The jobs:

| Job | Does | Permissions |
|---|---|---|
| `plan` | Checks the map: main only for a real run; each package is in the manifest; strict SemVer, no `v`; the tag and nuget.org version are free (or were made by this commit, for a re-run); the version is above every published one on its major.minor line; coupled packages released together. Writes `plan_json` | `contents: read` |
| `verify_ci` | The commit's `ci` check passed (a dry run only warns) | `checks: read` |
| `pack` | Writes `release-versions.props`, packs into `artifacts/<package>/`, runs the package checks and a NuGet consumer against it. A real run then attests build provenance for each package, its symbols and the assemblies inside it | `contents: read`, `id-token: write`, `attestations: write` |
| `publish` | Stages each package (and its symbols) on nuget.org with `dotnet package stage push` and trusted publishing, in the `release` environment. Nothing is public until a maintainer promotes it on nuget.org, signed in with 2FA. No checkout | `id-token: write` |
| `finalize` | Approved (`release-verify` environment) after promotion. Waits until each version is on nuget.org, tags `<package>/v<version>` (e.g. `Resultful/v0.2.0`) at the run's commit and creates its GitHub Release (a pre-release when the version has a `-`) No checkout | `contents: write` |

The logic is in `eng/build.cs` (`ReleasePlan`, `ReleaseProps`, `ReleaseCheckArtifacts`). `publish` and `finalize`
run no repository code. `pack` does, and can sign attestations: its OIDC token can't stage, because nuget.org's
policy is bound to the `release` environment.

Why:
- Committing a version meant a pull request per release, and lockstep republished packages that hadn't changed.
- Choosing versions at dispatch time lets a fix to one package ship alone, and keeps every release a deliberate,
  reviewed act (two environment approvals).
- Per-package tags and releases say exactly which package a tag is for once there is more than one.

Consequences:
- 0.2 pre-releases keep the zero-padded `betaNN` form until 0.2.0 is stable: NuGet compares labels without dots
  as text, so `beta.6` would sort below `beta05`. New pre-release lines use dotted labels (`beta.1`).
- Older releases were never tagged. 0.2.0-beta03 to beta05 were tagged `Resultful/v<version>` afterwards from
  publish times, version bumps and their DLLs; each tag message gives the evidence. `v<version>` tags would also
  count as `Resultful`'s.
- Attestations prove which repository, workflow and commit built a package: `gh attestation verify <file> --repo
  Resultful/Resultful`. nuget.org repository-signs packages, so verify a downloaded one by `lib/<tfm>/Resultful.dll`.
- nuget.org's trusted publishing policy for Resultful names `release.yml` and the `release` environment, and may
  only stage: a compromised workflow can submit a package for review but can't release it.
- Not yet seen on a real run: what staging the same version twice does, and how long an unpromoted package lasts.
  Before re-running `publish`, check nuget.org's staged packages.
- CI's packed consumers run against `0.0.0-dev`, which sorts below `0.2.0-beta05`; the upgrade check allows that
  downgrade warning (`NU1605`), since what it checks is the run-time binding.
- If `plan`, `pack` or `finalize` fails, "Re-run failed jobs" finishes the run: the tag and release steps accept
  what an earlier attempt made.
- A run that published but never tagged (cancelled, or its jobs can no longer be re-run) needs the tag and release
  made by hand: a new dispatch refuses a version that is already on nuget.org without its tag at that commit.
- The package validation baseline is the newest stable version on the line, so a pre-release of a line with no
  stable version yet (all of 0.2 today) has none. `CompatUpgrade` still checks a beta05 consumer at run time.
- The pack step packs only `Resultful`. A second package in the manifest needs `Pack` to pack each planned one.
- The first release's notes run from `Resultful/v0.2.0-beta05`.
