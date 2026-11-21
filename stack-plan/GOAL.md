# Goal: the Resultful roadmap as two stacks

> **2026-11-20, superseded in part by `AGENTS.md` on `main` (#57).** Where this file disagrees, `AGENTS.md` wins:
> one PR at a time on the real preceding branch (no drafting ahead, no stand-in cores); one squashed commit per PR,
> rebased onto its base and force-pushed (no `fixup!` commits, no merge commits, no cherry-pick catch-ups); build
> targets go in `eng/build.cs` (Cake.Sdk); SDK built-ins (`EnablePackageValidation`, PublicAPI analyzers) instead of
> custom gates; one shared PublicAPI pair per package; lockstep versioning; no TargetApi stubs, ledger or fixture
> trees: larger scenario tests in xunit.v3 plus CsCheck laws; PR bodies use `.github/PULL_REQUEST_TEMPLATE.md`
> (≤ 30 lines). The stacks were restacked by the maintainer; 04c/04d and the drafted-ahead PRs are closed.

Every implementer, reviewer and adversary reads this file first, and again after its context is compacted. The
orchestrator is the session that runs this goal, local or on Claude Code on the web. Claude subagents build and check each PR.
How each gate works is in `stack-plan/VERIFY.md`. Each PR's exact scope is in `stack-plan/contracts/NN.md`,
which the orchestrator writes just before the PR starts.

## Outcome

Two independent stacks of draft PRs, each PR based on the one before. Together they implement roadmap M1 to M6
(`docs/roadmap.md`) on top of the design in PR #34 (`rtkelly13/error-design`). Ryan reviews each stack in order, so
each PR must make sense on its own.

- **Stack A, 0.2 (01–04b).** Based on `main`. Its top branch, `stack-04b-release-0.2`, is ready to ship: Ryan
  merges it in order and publishes 0.2.0 whenever he chooses. Nothing in stack A knows about 0.3.
- **Stack B, 0.3 (00, 04c–25).** 00 (#34, the 0.3 design docs) moves to the bottom of stack B: it is rebased onto stack A's top and its PR base retargeted, so stack A holds nothing about 0.3. Stack B is based on stack A's top, because 0.3's gates reuse stack A's tooling
  (the characterisation suite, `release-check`, per-target versions). Once Ryan has squash-merged stack A, the
  orchestrator rebases stack B with `git rebase --onto origin/main stack-04b-release-0.2 <top> --update-refs`,
  so the stack A commits drop out, and retargets 00's PR at `main`. This is the one way the stacks aren't independent: B builds on A's tooling, and only becomes independent of A once A is merged. Until then, a fix that stack A needs is made
  on stack A and stack B is restacked on it.

The two stacks together do three things in order:
1. **01–04b (stack A) ship 0.2.0.** Today's API becomes a stable release, proven identical to `0.2.0-beta06` by binary.
2. **00, 04c–04e (stack B) write the 0.3 target as code and add the preview job.** The target public API, capability fixtures and a ledger, plus a
   reference app (the Shop) that uses all of it. Ryan reviews the target once, here.
3. **05–25 turn the target green.** Each PR flips named ledger lines from `pending` to `done`. The run is finished
   when no line is `pending` and every package's public API equals its target.

**The agents merge nothing and publish nothing.** No `gh pr merge`, no `rig gh pr land`, no NuGet push of any
kind, no `workflow_dispatch` of a publishing job. PRs stay drafts. Ryan alone merges, and publishes 0.2.0 when he
chooses. **0.3.0 previews are Ryan's too.** At checkpoints 19, 17 and 25, once every CI job on that PR passes and the
`publish-preview` dry run (04e) is green on the branch, the orchestrator comments on #39: the branch, the head SHA,
the preview number, the packages and versions the dry run packed, and the exact command
(`gh workflow run ci.yml --ref <branch> -f preview=true`, or whatever 04e names it). Ryan runs it. The run doesn't
wait: it carries on with the next PR. Numbers: `0.3.0-preview.1` (core, 19), `.2` (tooling, 17), `.3` (everything,
25). A preview is never deleted, only unlisted, so a failed one is followed by the next number, not re-pushed.
25 prepares a stable `0.3.0` (release notes, `Shipped` moved) for Ryan to publish.

**Horizon.** About 33 PRs is days of work, not hours. The run never pauses for review (Ryan, 2026-11-20): at each
checkpoint it writes a summary to `PROGRESS.md` and keeps going. Each checkpoint leaves something useful if the run
stops there: after 04b (0.2.0 is ready to ship), after 04d (the 0.3 target is reviewable), after 19 (the core is
finished), after 17 (the tooling is finished).

**The plan PR (#39, branch `rtkelly13/stack-plan`, never merged) is the working copy.** Everything in
`stack-plan/` lives on that branch: this file, `VERIFY.md`, `DECISIONS-TAKEN.md`, `PROGRESS.md`, the contracts, the
implementers' reports, the probes and the planning reviews. The orchestrator checks it out (a worktree at
`temp/wt/plan`), and commits and pushes every change to it with a one-line message, so #39's history is the run's
log. Other stack branches never contain `stack-plan/`. At each PR boundary it reads review comments on #39
(`gh pr view 39 --comments`, `gh api repos/Resultful/Resultful/pulls/39/comments`) and treats them as Ryan's
instructions. Never mark #39 ready, never merge it. To reach Ryan (a stop condition, a question), comment on #39
and say so in `PROGRESS.md`.

## Rules for every PR

1. **Contract.** The PR body opens with: the roadmap item and decision it implements, the ledger IDs it flips,
   *Later PRs* (what the contract leaves to which later PR), *Beyond the contract* (rule 13), *Decisions taken* (rule 6), the log line for 25 (rule 7), and links to the previous and next
   PRs.
2. **Base.** Each branch is `rtkelly13/stack-NN-<slug>`, based on the branch of the PR before it. Open it with
   `gh pr create --draft --base <previous branch>`.
3. **Done** means every CI job passes, the ledger IDs in the contract are `done` and nothing else changed state,
   and the orchestrator's own checks in `VERIFY.md` pass. If the run stops at PR k, PRs 00 to k each meet this.
4. **Light builds.** Build or test only the affected project (`dotnet build -m:2`); push and let CI run the
   matrix. Install the SDKs `global.json` needs (10.0.4xx and 11.0.100; 9.0.3xx for the compiler legs) with
   `dotnet-install.sh` if the machine lacks them.
5. **The design is the docs.** Decisions 1 to 16 and the explorations are the spec, including the proposed
   decisions 14 and 15 and decision 16 item 8: build them as written. From 04c on, the target API and the
   fixtures are the spec in code. Read the relevant decision before writing code.
6. **Ambiguity doesn't stop the run.** Take the default in `DECISIONS-TAKEN.md`. If there isn't one, take the
   smallest choice that keeps later PRs possible, and add it there and to the PR body.
7. **Docs move with code.** Update a decision's consequences when they change, and the README for public
   surface. Roadmap statuses and `docs/log.md` change only in 25: each PR puts its log line in its PR body. House
   style: short plain sentences, no marketing.
8. **Public API.** Every public change goes through `PublicAPI.Unshipped.txt`, and must already be in the target
   API (04c). A removal must be on `removed-in-0.3.txt`. Every package passes the package check.
9. **Tests.** xunit.v3 and CsCheck for new suites, NUnit for the existing suite (decision 6). Generator and
   analyzer behaviour follow `testing-the-shapes.md` and `building-the-generator.md`.
10. **The spec is protected.** An implementer changes only the `state` column of the ledger. A real change to a
    fixture or the target API is a separate `spec:` commit, a line in `DECISIONS-TAKEN.md` and a section in the PR
    body. The 0.2 characterisation suite in `tests/Resultful.Consumption` may be migrated by 08b, 09, 11 and 12 where the
    break is intended: each changed assertion is listed, and the count never falls. A PR never weakens a test, adds a `Skip`, a `NoWarn`, a `#pragma warning disable` or
    `continue-on-error` without listing it in the PR body.
11. **Git.**
    - Conventional commits. Never bare `git stash`.
    - Worktrees go under `temp/wt/NN`.
    - A reviewed, green PR is frozen. A later fix goes in a new PR at the top, unless it changes the API or the
      spec. Fixes that must go low are batched and restacked at the checkpoints, with
      `git rebase --update-refs`, then pushed with `--force-with-lease`.
    - Don't touch branches outside the stack. #36 and #37 are copied into stack branches, not rebased in place
      (another agent may have #37's branch checked out).
12. **Privacy.** Keep the maintainer's personal details out of commits, PR bodies and any external request.
13. **Improve your own area.** A contract is the floor. Each implementer goes past it inside its own PR's area:
    more tests, sharper gates, clearer errors, docs. It lists each addition under *Beyond the contract* in the PR
    body, and the reviewer judges whether it improves the project. Limits: it doesn't take another PR's item; in
    stack A it doesn't change `src/` or the public API; it stays under the size limit. What it finds but can't fix
    goes into `DECISIONS-TAKEN.md` with the PR that should own it (as 01's review sent the `void` `SwitchAsync` to
    19).
14. **Waiting on CI.** Poll `gh pr checks <n> --json name,bucket` in the background with a sleep of a minute or
    more. Never `--watch`. `rig` may not be installed; don't depend on it.

## Stop conditions (report to the orchestrator, don't push on)

- A decision in the docs contradicts itself, or contradicts code that must stay, in a way that changes public API.
- CI fails three times on the same PR for a cause that isn't understood.
- A `release-check` difference that isn't on a reviewed list.
- The `pending` count rises, or a `done` line goes red.
- A PR needs more than three `spec:` changes.
- A reviewer rejects the same PR three times, or a restack conflict isn't mechanical.
- A step would need anything the agents may not do (merge, a stable publish).

On a stop: comment on #39 with the cause and what was tried, mark the PR `blocked` in `PROGRESS.md`, and carry on
with any later PR that doesn't depend on it.

A PR that runs past 90 minutes is marked `blocked`, and the orchestrator reports before going on.

## The stack

| # | Branch slug | Implements | Done when (beyond rule 3) |
|---|---|---|---|
| 00 | `error-design` (#34, exists) | Docs: decisions 14–16, testing the shapes | already open |
| 01 | `consumption-tests` (from #36) | M1 characterisation | Today's public behaviour is pinned, both hazards included (`default` is a success; a bare string converts to an error). Runs on net10 and net11 and from the packed package. See the handover notes. |
| 02 | `test-infra` | M1 testing (decision 6) | `eng/run-tests.cs` finds and runs every test project. Coverage ratchet (core only). The reference model and its first laws. The counts and suppressions gates. The `release-check` scripts (`api-diff`, `il-diff`, `verify-package`), run against beta06 and empty. Spec and JSON tests also run under `tr-TR` (and invariant), set per test run, since 0.2's behaviour must hold under any culture. |
| 03 | `analysis-level` | M1 quality | `AnalysisLevel latest-all` with explicit opt-outs, XML docs on public members, a nullable audit, per-target `PublicAPI` files. Core edits are annotations and docs only: the IL list stays empty, each re-annotation is on the API list. |
| 04 | `per-package-release` | M1, decision 11 | A version per project, change detection by path and project graph, a tag per package, the package check on every packable project. `eng/release-plan.cs` tested against made-up change lists. Nothing publishes. |
| 04b | `release-0.2` | Ship today's API | `0.2.0`, no new API. `Unshipped` moves to `Shipped`, release notes are written. The `release-check` job's five checks pass against beta06, with the consumers in `VERIFY.md` (net8, net10, net11, `net481` on Windows, Paket `strategy: min`). Mutation run 0. **Checkpoint: 0.2.0 is ready.** |
| 04c | `spec-harness` | the 0.3 target | `tests/Resultful.TargetApi` (every package's end-state API as `throw null` stubs), `tests/Resultful.Spec` (fixtures, `SpecHarness.cs`), `ledger.tsv` with every line strict `pending`, the API gate (`Unshipped` additions ⊆ target, plus `removed-in-0.3.txt` for 0.2 API the target drops, empty by 12),. Each gate has a self-test that shows it can fail. Every default in `DECISIONS-TAKEN.md` is visible in the target. Includes decision 17 (error unions on .NET 11, alongside `[ClosedHierarchy]`) and its `UERR-*` fixtures, from `stack-plan/probes/union-error-probe`. Round 4: the JSON read contract for unions and hierarchies (unknown `type` → `JsonException`; two leaves with the same simple name in one hierarchy or union → generator error), nested-union `.Error`/`WithContext`/`TryFind` fixtures, `ValidationFailed`/`Validation` equality, `Error.ToString`, `Fail(null!)` and an empty `Validation` failure, all in the target. |
| 04d | `reference-app` | the 0.3 oracle | The Shop (`samples/Resultful.Shop.*`) against the target API, one folder per use case UC01–UC10, built only when its line is `done`. `tests/Resultful.EndToEnd` with the packed-package restore, `http-cases.json`, the broken consumers, and the doc-snippet check (lenient). **Checkpoint: the 0.3 target is reviewable.** |
| 04e | `publish-preview` | previews | A `publish-preview` job in `ci.yml`: `workflow_dispatch` only, from a `rtkelly13/stack-*` branch, uses the `nuget-preview` environment, and refuses unless every packed version contains `-preview.`. The stable publish job is unchanged and still `main` only. Its own `concurrency` group with `cancel-in-progress: false`, so a push to the branch can't cancel a push to NuGet midway. Lives in `ci.yml`, which already exists on `main`, so it can be dispatched from a stack branch. Tested with a dry run that packs and checks the versions without pushing. Ryan creates the `nuget-preview` environment and its secret. All packable packages get the same `0.3.0-preview.N`, and the dry run restores the set together. The dry run also dispatches the job live from the stack branch with `gh workflow run ci.yml --ref`; if GitHub rejects it, the preview job moves into stack A (04) so it is on `main`. The agents never dispatch it with a push: they run its dry run in CI and hand Ryan the command (see *The agents merge nothing*). |
| 13a | `generator-target` (from #37) | M4, decision 16 | The generator compiles against the target API, the stub deleted. Fuzz and determinism tests pass. Not packed (`IsPackable=false`). `GEN-*` (the `RFG0001`–`RFG0006` diagnostics, compile-only) flip here; `SHAPE-*` and `HIER-*` stay `pending`. |
| 05 | `named-methods` | M2 | `IsSuccess`/`IsFailure` with `[MemberNotNullWhen]`, `TryGetValue`/`TryGetError`, `Deconstruct`, `OrElse`, `ValueOr`, on every target. `GenerateDocumentationFile` on every `src/` project with CS1591 an error: every public member is documented (stack B only, because adding the XML file to the package would fail 0.2's package check). |
| 06 | `error-record` | decision 14 | The abstract `Error` record: `Message`, `Cause`, `Causes()`, `TryFind`, `WithContext` that keeps the type, `ValidationFailed`. No implicit conversion from `string`. `Context` is `IReadOnlyList<string>`, copied on add. `Error` equality is structural, `Context` compared element by element, so two identical errors are equal before and after `WithContext`; laws in the reference model. `Error.ToString()` is `sealed` in the base: leaf type name, `Message`, `Context` and the cause chain, never the leaf's own properties (they may hold personal data). `ERR-TOSTRING`. |
| 07 | `attributes` | decisions 15, 16 | `Resultful.CodeAnalysis`: `MustObserve`, `ClosedHierarchy`, `ResultShape`, applied to the core. `IResultShape<T, TError>`, `IValidationShape<T, TError>`. `IErrorUnion` (decision 17) ships here on every TFM, so `PublicAPI` is the same everywhere. |
| 08a | `own-storage` | M3, decision 1 | Every type keeps its own state with a state byte. No behaviour change: the baseline differential against 0.2.0 is empty. Struct sizes and allocations are tested. |
| 08b | `zero-guard` | decision 10 | Zero means uninitialised and throws on use. `[Obsolete(error: true)]` parameterless constructors. The baseline differential's allowlist holds only the zero value. `Fail` rejects an error union with no value. Zero-allocation check, verified in `stack-plan/probes/union-probe-2`: a cached `IsUnion` flag, then the union's single reference field read with `Unsafe.As` when `Unsafe.SizeOf<TError>()` is pointer-sized, else `IUnion { Value: null }` (24 B, boxes). A test pins the layout so a compiler change fails loudly. `net11.0` only (`IUnion` doesn't exist below it). Only reading the value or error of a zero value throws. `ToString` (`"<default Result>"`), `Equals`, `GetHashCode` and `[DebuggerDisplay]` never throw, so logging, dictionaries and the debugger survive the bug. The `Unsafe.As` path runs only when `TError` is a compiler-generated union whose one instance field is `object` (checked by reflection once per type and cached, plus `RuntimeHelpers.IsReferenceOrContainsReferences<TError>()`); a hand-written `IUnion` struct, e.g. one holding a `long`, takes the `IUnion` path. A fixture with such a struct proves no crash (`UERR-CUSTOM-IUNION`). `Fail(null!)` throws `ArgumentNullException` (`ZERO-NULL-ERR`). |
| 09 | `result-error` | M3, decision 14 | `Result<T>` holds an `Error` instead of `IEnumerable<string>`. |
| 10 | `validation` | M2, decision 14 | `Validation<T, TError>` on its own storage: a non-empty list, `Combine`, `AsResult`/`ToResult(combine)`. `VoidResult` converts to `Result<Unit>`. `Validation.Fail` with no errors throws (`VAL-NONEMPTY`). `ValidationFailed<TError>` and `Validation<T, TError>` compare their errors element by element (`VAL-EQ`). |
| 11 | `oneof-split` | M3, decision 1 | No OneOf in the core. `Resultful.OneOf` interop package, `Resultful.None`. The core resolves alone under Paket `strategy: min`. |
| 12 | `void-to-unit` | M3, decision 14 | `VoidResult` is `[Obsolete]`, `Result.Ok()` returns `Result<Unit>`, `Unit` as decided in 04c. `Shop.Legacy` fails with exactly the listed compiler errors (CS0029, CS0411, CS0618); its analyzer findings flip in 16a/16b. Release notes carry the migration. |
| 12b | `text-json` | decision 11, testing | `Resultful.Text.Json`: AOT-safe, source-generation-friendly converters for every core type, with a wire format fixed in 04c and round-trip laws. `JsonAssert.Equal("""…""", value)` in a shared test project: serialise with fixed options, compare with `JsonNode.DeepEquals`, print both on failure. From here on, tests may pin a result's whole structure (case, value, error type, message, context, cause chain) as a raw string literal. An error union writes as its leaf. Reading: a union or hierarchy is read back through the generator's case list, with no reflection (AOT-safe); an unknown `type` throws `JsonException`. `UERR-JSON-*` round-trips flat and nested unions and pins the unknown-type failure. |
| 18 | `operators` | M2, decision 9 | `Resultful.Operators` over the named methods, laws tested, a precedence table in the README. |
| 19 | `async` | M2 | `ValueTask` overloads, `IAsyncEnumerable` helpers, `CancellationToken` where it applies. Sync and async equivalence laws. **Checkpoint: the core equals its target API, except `UNION-*` (24). Mutation run 1.** Exceptions and cancellation propagate: nothing in core catches implicitly, including `OperationCanceledException` in async overloads; `Result.Try` is the opt-in. CA2007 (`ConfigureAwait(false)`) is an error in `src/`. The instance `SwitchAsync` on all four 0.2 result types returns `void` (fire-and-forget: the callback's task is dropped and its exception lost; pinned by 01). In 0.3 it returns `Task`, awaited; `ASYNC-SWITCH-TASK`, and a migration note. |
| 13b | `generator-package` | M4, decision 16 | The generator against the real core. One build per Roslyn version in the package. The four compiler legs pass on `Shop.Domain` and `Broken/Generator/*` (17 adds the analyzers, 20b `Shop.Api`). The bare MSBuild compile (`generated_code=false`, ImplicitUsings off, `IsAotCompatible`). The IL and member budgets. The generated-member walk. Seeded faults are all caught. `SHAPE-*` and `HIER-*` flip, including Tier 1 `Match`/`Switch`/`TryMap`. A `union` as a shape's error type gives the same flat members; `RFG0007` for a case that isn't an `Error`; `UERR-*` shape lines flip. A nested error union stays one case (not flattened): the shape's cases are the union's direct cases, each with an implicit conversion, and a `PaymentError` case is handled with its own inner switch. Leaves of a nested union also convert into the shape implicitly (a bare `Result<T, OrderError>` needs `(OrderError)leaf`, because C# won't chain leaf → union → result; CS0029, verified). Adding an error case changes `Match`'s signature, a binary break for callers in other assemblies (`MissingMethodException`): the API diff reports it as breaking and the generator docs say so. For a nested union, `IErrorUnion.Error` unwraps the inner union (`Value switch { Error e => e, IErrorUnion u => u.Error, … }`), and `WithContext` dispatches per direct case and rewraps, so the inner union's type is kept. `UERR-NESTED-*`. |
| 15 | `shape-conversions` | decision 16 items 4, 5, 7 | Conversions into a shape (from another shape, from `Validation`). `WithContext` keeps the shape. `ManyErrors` complete. A shape whose error is a union widens implicitly to `Result<T>` through `IErrorUnion.Error` (the leaf). |
| 16a | `analyzers-core` | M4 | `Resultful.Analyzers`, per Roslyn build: a result never observed, a `default` result. Also `default` of an error union. `RF0001` covers `Task<Result>`/`ValueTask<Result>` (fire-and-forget, unawaited) and `async void` lambdas passed to `Switch`; only an explicit `_ =` discard passes. |
| 16b | `analyzers-more` | M4 | `Value` without a check, a bare string into `Result<string>`, a `Match` that should be a `switch`, the unassigned result field (decision 10 layer 3), the `private protected` suggestion. Code fixes where unambiguous. `RF0008`: every handler passed to `Match`/`Switch`/`TryMap` is explicitly typed or a method group (error by default), with a fix-all code fix; the Shop and all docs snippets use typed handlers. `RF0009`: `Map` whose selector returns a result (`Result<Result<T>>`), code fix to `Bind`. `RF0008` covers only the generated multi-case `Match`/`Switch`/`TryMap`, whose parameter order can change. Core's two-handler `Match(onSuccess, onError)` has a fixed order and is exempt. |
| 17 | `closed-hierarchy-rules` | decision 15 | The CS8509 suppressor with the `null`-arm rule, the rule for a type in another assembly, the `.editorconfig` list for third-party types. `Resultful.Analyzers` and `Broken/Analyzers/*` join the four legs; the broken consumers fail as listed. **Checkpoint: the tooling is finished. Mutation run 2.** |
| 20a | `aspnetcore-errors` | M5 | `Resultful.AspNetCore`: the error-to-status map, ProblemDetails, `ValidationFailed` as 400. A 500 body has a fixed title and no `Message` outside the Development environment, so internal details don't leak. |
| 20b | `aspnetcore-endpoints` | M5 | Typed results, an endpoint filter, the Minimal API validation bridge. `Shop.Api` joins the four legs. The Shop's HTTP cases and the OpenAPI check pass. |
| 21 | `fluentvalidation-diagnostics` | M5 | `Resultful.FluentValidation` and `Resultful.Diagnostics`. |
| 21b | `fsharp` | decision 19 | `Resultful.FSharp`, **interop only** (maintainer, 2026-11-21): conversions both ways between core results, `Option<T>`, `Validation` and `FSharp.Core`'s `Result`, `option`, `voption`. Written in C# (PublicAPI, docs, package validation apply). No CE, module, active patterns or assertions. C# laws (CsCheck round-trips) plus one small F# test project proving the conversions read well from F#. |
| 21c | `assertions` | decision 18 | `Resultful.Assertions` (`ShouldSucceed`, `ShouldFail`, `ShouldFailWith<TError>`, no assertion-library dependency), `Resultful.Assertions.AwesomeAssertions`, `Resultful.Assertions.FluentAssertions` (7.x only). Work on shapes, hierarchies and net11 error unions. Failure messages use `Error.ToString()`. The core's law tests don't use them; the Shop (23a) does. |
| 20c | `hotchocolate` | decision 19 | `Resultful.HotChocolate`, after 20b: resolvers return `Result<T, TError>` or a shape; leaves become GraphQL error types implementing `Error { message }` (query conventions: `T | leaves` union; mutations: payload `errors`), read from the generator's case list without reflection; nested unions flattened in the schema; `ValidationFailed` → `ValidationError`; undeclared errors → top-level `errors` with `extensions.code` and no message outside Development; `ToFieldResult()`. A GraphQL endpoint on the Shop with cases like the HTTP ones. |
| 22 | `aot` | M5 | The Shop published native AOT on `net11.0` answers the HTTP cases, with no IL2xxx or IL3xxx warnings. The Shop's JSON goes through `Resultful.Text.Json`'s source-generated context. |
| 23a | `finish-the-shop` | M5, testing-the-shapes layer 7 | UC01–UC10 are `done`. Plus a `net11.0`-only Shop folder whose errors are unions. |
| 23b | `samples-integrations` | M5 | Samples for MediatR, Polly and EF Core. |
| 24 | `core-union` | M6, decision 3 | `[Union]` on the core result types on net11. The correctness matrix. BenchmarkDotNet allocations for net10 against net11, recorded in a new decision. |
| 24b | `early-return` | decision 22 (proposed, #84) | `Failed(out value, out error)` on every result type and `Failed(out value, out errors)` on `Validation`, nullable-flow annotated, zero value throws; the missing `TError` → `Result<T, TError>` conversion; RF0001/RF0003 recognise `Failed`; a rule for a value-type `value` used in the failed branch. Laws against `Match`. |
| 24c | `comprehensions` | decision 22 | `Select`/`SelectMany` over `Task`/`ValueTask<Result<…>>` mixed with sync steps; query syntax on generated shapes keeping the shape type; `Result.Ensure(condition, error)` with `[CallerArgumentExpression]`; `Traverse`/`Sequence` and `TraverseAll`/`SequenceAll` (sync and async, concurrency limit); `params ReadOnlySpan` `Validation.Combine`. Checks: `[CollectionBuilder]` for failures, static abstract `IResult<TSelf, T, TError>`. No `where`, no query syntax over `Validation`. |
| 24d | `async-streams` | decision 22 | net10+ in the core behind `#if`: `SelectResultAsync`, `Successes`, `Failures`, `PartitionAsync`, `CollectAsync`, `CollectAllAsync`, `TraverseAsync` on `IAsyncEnumerable<Result<T>>`; `Resultful.AspNetCore` answers `Result<IAsyncEnumerable<T>>` (ProblemDetails before the first item, streamed after). Laziness, disposal and cancellation tests; the Shop worker (UC6) uses them. |
| 24e | `await-probe` | decision 22 | A probe of `await` on a result as a short-circuit via a method-level async builder: compiles, allocations, debugger and analyzer behaviour, mixing real awaits. A findings note; no package API. Shipping it needs a new decision. |
| 25 | `docs-sync` | all | Roadmap statuses and `docs/log.md` for the whole stack. No `pending` line. Every package equals its target. The snippet check is strict. Mutation run 3. The docs state that adding an error case is a breaking change for libraries that expose a shape. |

The order puts the core (05–12b, 18, 19) before the tooling, so the generator and analyzers build on a finished
core. This differs from the "Order of work" in `testing-the-shapes.md`; 25 updates that section.

Out of scope: collection operators (still being explored), M7's API review, a documentation site, community
files, and the OneOf/union packages in the `Resultful.Assertions` repository (decision 18 moves only Resultful's own
assertions here, as 21c).

A PR may split into `NNa`/`NNb` if it passes about 800 changed lines outside tests. Two tiny PRs may merge.
Record either in `PROGRESS.md`.

## Agents

- **Orchestrator** (main session). Writes no product code. Owns `PROGRESS.md`, the contracts, every restack, and
  the checks in `VERIFY.md`. It never accepts "tests pass" as a report.
- **Implementer.** One per PR, in a worktree at `temp/wt/NN` (gitignored). Gets the contract. Returns `stack-plan/reports/NN.json`: a
  list of claims.
- **Reviewer.** Fresh. Gets the contract, the relevant decisions and `gh pr diff`. Never the implementer's report.
  Checks first, from 01's review: tests that can't fail (a flag already set by an earlier step, an
  `Assert.IsType` the static type already guarantees, a call with no assertion after it); async calls not awaited,
  or not awaitable because the API returns `void`; a packed-package run that could restore a published package
  instead of the one just built; comments that disagree with the code. Rank findings, give each a failure scenario
  and a fix; the orchestrator sends them back to the implementer.
- **Adversary.** For 03, 04b, 08a, 08b, 09, 13b, 15, 16a, 16b and 17. Writes a fixture the spec says should
  pass and that fails. For 03 and 04b it hunts for any difference from beta06. A valid find becomes a fixture.

At most two implementers at once. Only 04c and 04d may be drafted alongside 02–04, and only as new directories
with no edits to root files (`Resultful.slnx`, `Directory.Packages.props`, `.github/`, `.ls-lint.yml`). They are
wired in after rebasing onto 04b. Everything else is built in stack order.

**Pipelining (Ryan, 2026-11-20: don't wait longer than needed).** The order stays linear; the waiting doesn't.
- **Start the next PR when this one is pushed, not when it's green.** PR n+1 branches from n's pushed head as soon
  as n's implementer has pushed and its local build passes.
- **Append only (Ryan, 2026-11-20). No force-push, no rebase of a pushed branch, no restack in this run.** A fix to
  PR n is a new commit on n's branch, with the subject `fixup! <subject of the commit it fixes>` so that a later
  `git rebase --autosquash` folds it in deterministically. A branch above n that needs the fix gets the same commit
  cherry-picked (`git cherry-pick -x`, keeping the subject) as a new commit; one that doesn't need it yet is left
  alone. A draft-ahead branch stays on the base it started from. PR bases stay as opened. **A PR that conflicts with its base gets no CI** (GitHub skips `pull_request` runs on a conflicting PR), so when a base branch gains commits that conflict, the branch above cherry-picks the base's new commits (`-x`) and resolves them, in stack order; duplicates that come out empty are skipped. After the run, a local
  agent rebuilds the whole tree into a clean stack from `stack-plan/RESTACK.md`, which the orchestrator keeps: for
  each branch, the commit it started from, the commits it owns, and the `fixup!` and cherry-picked commits it carries.
  **No merge commits** on any branch (linear history), with one narrow exception: when cherry-picks can't clear a
  conflict with the base (the merge base stays old, so a file both sides rewrote always conflicts), a merge of the
  base's head that changes no files (`git diff HEAD^1 HEAD` empty) may be added so GitHub runs CI. Each such merge is
  listed in `RESTACK.md`; the local pass drops it, and the squash merge into `main` never carries it. So far: 05
  (2104c15), 04b (edfbfdf). Agents keep working files in
  `temp/` inside their worktree, never in the shared scratchpad.
  This replaces the restack steps elsewhere in this file (rule 11, the stack B rebase onto stack A, wiring 04c/04d
  "after rebasing onto 04b"): they happen in that local pass, not in the run.
- **Review runs alongside CI.** The reviewer starts on the pushed diff while CI runs; the adversary (where there is
  one) starts with the reviewer. Fixes from both are batched into one push.
- **Implementers don't wait on CI.** An implementer pushes, opens the draft and reports. The orchestrator watches CI
  and hands a red job to a fixer agent with the log, so the implementer slot is free for the next PR.
- **Contracts one or two ahead.** The orchestrator writes the next contracts while PRs build.
- **Up to four agents building at once** (implementers and fixers; reviewers and adversaries don't count; the
  session has four cores). A PR is still *done* only by rule 3, and a checkpoint only when
  every PR under it is.
- **Drafting ahead (Ryan, 2026-11-20: as much in parallel as possible).** Once 04c's target API is pushed, a PR
  whose code lives in its own new directory may be drafted against the target stubs before its turn: 13a
  (generator), 12b (`Resultful.Text.Json`), 16a/16b (analyzers), 20a/20b
  (`Resultful.AspNetCore`), 21 (`Resultful.FluentValidation`, `Resultful.Diagnostics`). Same no-root-files rule.
  Each is rebased into its stack position when its turn comes, switched from the stubs to the real projects, and
  only then can it be *done*. The core chain (05–12, 18, 19, and 24; 18 too, since 04c found `Resultful.Operators` is a namespace in the core) edits the same files and stays strictly in order.
  If Ryan's review of 04c changes the target, drafted PRs are reworked; that risk is accepted.

## Known traps

- **Running tests.** `dotnet test Resultful.slnx` fails once an xunit.v3 Microsoft.Testing.Platform project is in
  the solution (`Microsoft.Testing.Platform.MSBuild.targets:395`). `eng/run-tests.cs` (02) finds
  `tests/*/*.csproj`, runs NUnit projects with `dotnet test` and MTP `Exe` projects with `dotnet run`.
- **Dependencies follow the target.** In `Directory.Packages.props`, a `PackageVersion` is conditioned on
  `$(TargetFramework)` where versions differ: `Microsoft.AspNetCore.*` and `Microsoft.Extensions.*` are 10.x on
  `net10.0` and 11.x on `net11.0`. The core has no dependencies on any target.
- **One generator and analyzer build per Roslyn version.**
  - Folders: `analyzers/dotnet/roslyn4.14/cs/` and `analyzers/dotnet/roslyn5.x/cs/` (exact names checked in 13b).
  - Measured here: SDK 9.0.315 is Roslyn 4.14.0, SDK 10.0.401 is 5.9.0, SDK 11.0.100 is 5.12.0. So SDK 10 loads
    the 5.x build too.
  - The 4.14 build never emits `[Union]`. The 5.x build decides **at run time, from the compilation**: the
    attribute exists and the language version allows it. Never by `#if` or compiler version.
  - The 5.x build compiles against the lowest 5.x it supports, or older SDKs report CS9057.
  - Each test project references the Roslyn version of the build it tests.
- **Stale packages.** Packed runs use `-p:VersionSuffix=ci.$GITHUB_RUN_ID`, an isolated `--packages` folder and
  package source mapping that sends `Resultful*` to `artifacts/` only.
- **CI load.** Every workflow has `concurrency: cancel-in-progress` per branch. Restacks are batched.
- **Lint.** `.ls-lint.yml` needs `| regex:api` for `src/*/api/`.

## Handover notes

**01, from #36 (`rtkelly13/consumption-tests`):**
- `ResultfulPackageVersion` is hard-coded to `0.2.0-beta06`. Read it from `src/Resultful/Resultful.csproj`.
- `PublicApiCoverageTests.cs` counts `CandidateSymbols`, so an overload that failed to bind counts as covered.
  Remove that. Read `Unshipped` as well as `Shipped`, or the gate sees no new API.
- `ResultExtensionsSyncTests.cs` and `ResultExtensionsAsyncTests.cs` mostly test the happy path. Add failure-path
  and mixed-input tests.
- `NetStandardLoadTests.cs` prefers `src/Resultful/bin/...` over the package asset. Reverse that when
  `UsePackedPackage=true`.
- `TargetDesignSpecTests.cs` has six skipped tests with empty bodies, and `StandInCore` is a string stand-in.
  Delete both: 04c replaces them.

**13a, from #37 (`rtkelly13/generator-experiment`):**
- `ResultShapeEmitter.cs` calls these core members by name: `IsDefault`, `TryGetValue`, `TryGetError`,
  `TryGetErrors`, `AsResult()`, `IResultShape<T, TError>`, `IValidationShape<T, TError>`. The target API uses
  those names.
- `GeneratorTestHarness.cs`'s `CoreStubSource` is replaced by the target API, and the harness merges into
  `SpecHarness.cs`.
- The IL already measured is in `api/emitted-api-budgets.txt`: `TryGetValue(out Leaf)` is 36 bytes, `Match` is
  `38 + 24L`, `TryMap` is `63 + 24L`, and nothing boxes outside `get_Value`.

## Files

- `stack-plan/GOAL.md`: this file.
- `stack-plan/VERIFY.md`: every gate, and what the orchestrator checks itself.
- `stack-plan/contracts/NN.md`: one PR's IDs, paths and gate commands.
- `stack-plan/reports/NN.json`: an implementer's claims.
- `stack-plan/PROGRESS.md`: one line per PR: number, branch, PR link, state (`todo`, `building`, `review`,
  `green`, `blocked`), note.
- `stack-plan/DECISIONS-TAKEN.md`: each choice made where the docs were silent.
- `stack-plan/probes/`: the union probes behind decision 17 and the Paket consumer for `release-check`.
- `stack-plan/reviews/`: the planning reviews the plan was merged from (history, not instructions).
