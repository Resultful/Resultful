# Common brief for the decision-22 PRs (24b, 24c, 24d, 24e)

Repo: /home/user/Resultful (Resultful/Resultful on GitHub). You work in your own git worktree; never touch another
agent's worktree or branch.

## Rules (read these files first)
- `git show origin/main:AGENTS.md` — governing rules; they override everything else. Key points: Cake.Sdk
  `eng/build.cs` is the single build entry point (add targets there, no new standalone eng/*.cs); SDK built-ins
  (EnablePackageValidation, TreatWarningsAsErrors, PublicApiAnalyzers) over custom gates; lockstep versioning (one
  version in Directory.Build.props); one shared PublicAPI pair per package at the project root; no micro-file
  explosions; xUnit v3 scenario suites + CsCheck property laws; Roslyn compile harnesses only for diagnostics;
  PR body ≤ 30 lines using `.github/PULL_REQUEST_TEMPLATE.md`; title ≤ 72 chars, conventional commit style.
- `git show origin/rtkelly13/stack-plan:stack-plan/HANDOVER.md` and `.../WEB-RUN.md` (environment notes).
- Decision 22 and its exploration (not on your base yet; read them from the docs PR branch):
  `git show origin/rtkelly13/docs-native-functional:docs/decisions/0022-native-functional-paths.md`
  `git show origin/rtkelly13/docs-native-functional:docs/explorations/native-functional-csharp.md`
- Your PR's row in `git show origin/rtkelly13/stack-plan:stack-plan/GOAL.md` (rows 24b, 24c, 24d, 24e).
- The base's existing projects for conventions: `src/Resultful.AspNetCore`, `src/Resultful.Text.Json`,
  `tests/Resultful.AspNetCore.Tests`, `tests/Resultful.Text.Json.Tests`, `tests/Resultful.Generators.Tests`
  (how a test project references the source generator), `Directory.Packages.props`, `Resultful.slnx`,
  `eng/build.cs`, `.github/workflows/ci.yml`, `docs/architecture.md`.

## Base and parallelism
- Base: `origin/rtkelly13/stack-20a-aspnetcore-errors` (PR #76, the real top of stack B; real core, no stand-ins).
  Four decision-22 PRs are built in parallel as siblings on this base (as #78–#81 were); restacked later
  (24b → 24c → 24d → 24e, above 24). To keep that restack mechanical:
  - Put everything you can in your own new directories.
  - Shared files (`Directory.Packages.props`, `Resultful.slnx`, `ci.yml`, `eng/build.cs`, `.ls-lint.yml`,
    `docs/architecture.md`, `README.md`): smallest possible edit, in your own contiguous block with a comment
    naming your package, never reordering or reformatting existing lines.
  - These PRs DO edit the core (`src/Resultful`). Own only the files your brief names; put new members in NEW files
    (e.g. `src/Resultful/Failed.cs`, `src/Resultful/LINQ/AsyncQuerySyntax.cs`) rather than editing shared files.
    `PublicAPI.Unshipped.txt`: add your lines (the analyzer's code fix sorts them; that's fine — the restack takes
    the union of lines).
  - Ownership boundaries between the four: 24b owns `Failed`, the `TError` conversion and the analyzer changes;
    24c owns query syntax (sync/async/shapes), `Ensure`, `Traverse`/`Sequence`(+All, +Async over IEnumerable) and
    `Combine` params; 24d owns everything over `IAsyncEnumerable` and the AspNetCore stream result; 24e owns only
    its probe. Don't implement another PR's members; if you need one, write against the planned name in a test
    comment and say so in your report.
- Environment: `export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet` in every shell command (SDKs 9.0.315,
  10.0.401, 11.0.100 are installed). Build with `-m:2`, only the affected projects. NuGet.org is reachable.
  chillicream.com is NOT reachable; raw.githubusercontent.com and `gh api -X GET search/code` are.
- Use GitHub MCP tools (load with ToolSearch) for PRs and checks; no `gh` except search.

## Quality bar for the test suite (the user asked for this explicitly)
- Every public member has a test that would fail if its behaviour changed: success and failure paths, null/edge
  inputs, exact failure messages where messages are part of the contract.
- CsCheck property laws wherever there is an algebraic claim (round-trips, agreement with the core's `Match`,
  associativity/identity laws, accumulation laws). Use the core's real types and its generators if the base has
  some (look in tests/), else write small `Gen<>`s in your test project.
- Scenario tests that look like real use (multi-step: validation → domain error → async → output), not only units.
- Tests on every target framework the package ships (net10.0 and net11.0 at least; net11-only behaviour such as
  C# 15 error unions tested under `#if NET11_0_OR_GREATER` or a net11-only project).
- Personal-data rule: no output (messages, logs, HTTP bodies, `Ensure` default messages) ever contains a leaf error's
  own properties or runtime values; test it with a sentinel string.
- No `Skip`, no `NoWarn`, no `#pragma warning disable` without a written reason in the PR body's API Impact or
  What to Review section.
- Run the full new test projects locally on both net10.0 and net11.0 before pushing; report counts.

## Delivery
- One squashed commit per PR (amend + `git push --force-with-lease` on YOUR new branch only). Commit message ends
  with:
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Mdbr21q451qU4ss6jyAnU2
- Before pushing: `git diff --stat origin/rtkelly13/stack-20a-aspnetcore-errors..HEAD`; every file must belong to
  your PR.
- Open a DRAFT PR, base `rtkelly13/stack-20a-aspnetcore-errors`, using the template. In Stack: "Prev: #76 (decision 22; built
  in parallel with 24b–24e as siblings; restacked linearly later)". End the body with:
  🤖 Generated with [Claude Code](https://claude.com/claude-code)

  https://claude.ai/code/session_01Mdbr21q451qU4ss6jyAnU2
- No personal details (names, emails) in commits or PR bodies. Never merge, never publish, never push to NuGet,
  never dispatch workflows.
- Wait for CI's first result (background `sleep 240`, then `get_check_runs`); fix failures and re-push until green
  or until you can name a blocker you can't fix.
- Final report (your last message): PR number and URL, branch, head SHA, CI state, test counts per TFM, the list of
  shared files you touched, anything you deferred and why, and any decision you made that the brief didn't cover.
