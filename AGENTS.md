# RESULTFUL

## Repository & Git Conventions

1. **Squash Merge Only**: All pull requests merge into `main` using **Squash and Merge** exclusively.
2. **Delete Branch on Merge**: Feature branches are deleted immediately upon merge into `main`.
3. **Linear History**: Maintain a strictly linear history. Rebase onto the base branch before pushing; **no merge commits** and **no unsquashed `fixup!` commits**.
4. **Direct Push Protection**: Non-force direct pushes to `main` are blocked; PR mechanism required.
5. **Local Temp & Worktree Directory**: All temporary files, scratch files, and git worktrees MUST go inside `/temp/` (gitignored).
6. **Gitignored Local TODO File**: Keep local task tracking in a root gitignored `TODO.md`.

## Build & Tooling Guardrails

- **Single Cake.Sdk entry point:** All build, test, pack, preview, and compatibility orchestration lives in `eng/build.cs` (`#:sdk Cake.Sdk@7.0.0`). Add new targets there instead of creating standalone `eng/*.cs` scripts.
- **Prefer SDK built-ins over custom gates:** Use `<EnablePackageValidation>true</EnablePackageValidation>`, `TreatWarningsAsErrors`, and Roslyn `PublicApiAnalyzers` instead of custom API/IL diff scripts, test-count/coverage JSON ratchets, or PR-body suppression checkers.
- **Lockstep versioning:** All `Resultful.*` packages share one version in `Directory.Build.props` and one release tag.

## Public API & File Hygiene

- **One shared PublicAPI pair per package:** Keep `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` at the package project root. Add a TFM-specific delta file (`PublicApi/{Net,NetStandard}/`) **only** for the few lines that genuinely differ by TFM (e.g. covariant `<Clone>$()` or `net11.0` `[Union]`) — never duplicate full API files per TFM.
- **No micro-file explosions:** Keep related domain types, tests, and samples in cohesive files. Never create 10-line 1-case-per-folder fixture trees, `TargetApi` throw-null stub projects, `ledger.tsv` trackers, or fake `GeneratorStandIn/*.g.cs` files.

## Testing Guardrails

- **One test project per satellite:** Keep at most **one** test project per satellite package (`tests/Resultful.<Satellite>.Tests`). Do not split a thin satellite across 2–3 separate `.csproj`/`.fsproj` test projects; keep shared test helpers in a single shared helper project or compile link.
- **Larger, real-world scenario tests:** Write normal compiled xUnit v3 scenario suites exercising multi-step pipelines (validation → domain errors → async composition → JSON/HTTP), plus CsCheck property laws and packed-package consumer checks (`tests/Compatibility`, cohesive `samples/Resultful.Shop`). For thin satellites (`<500` LOC), keep 2–3 cohesive files (`Scenarios.cs`, `Laws.cs`) rather than mirroring every helper in its own file.
- **Readable, structural assertions:** Prefer whole-structure assertions over 6–10 line assertion blocks — use `JsonAssert.Equal` for JSON/ProblemDetails payloads, projected tuple tables instead of parallel `.Select(...)` arrays, small snapshot records for multi-field spans/responses, and dogfood `Resultful.Assertions` across satellite tests once available (core law tests keep using `Match` directly).
- **Roslyn compile harnesses for diagnostics only:** Reserve in-process Roslyn compilation tests strictly for source-generator output/budgets and negative-compile or analyzer diagnostics (`RFG*`, `RF*`, `CS8509`).

## PR Hygiene

- **Linear vertical slices:** Work on one PR at a time against the real preceding branch. Never open drafted-ahead PRs with throwaway stand-in cores.
- **Scope check before push:** Run `git diff --stat <base>..HEAD` and verify every changed file belongs to the PR's scope.
- **Concise PRs:** Title ≤ 72 chars. Body ≤ 30 lines / ≤ 2 KB using `.github/PULL_REQUEST_TEMPLATE.md`. Never put local test logs, review-fix diaries, or SHA tables in PR bodies.
