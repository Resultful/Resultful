# Round 2 Review: Executable Spec, End-to-End Oracle, and Autonomous Pipeline

## 1. Executable Specification Written Up Front (PR 01b)

Don't leave M2–M6 acceptance criteria as empty `[Fact(Skip = ...)]` placeholders. Land an executable spec right after PR 01 (`01b: executable-spec`) that compiles today against a **ledger** and turns green PR by PR:

- **Spec ledger (`tests/Resultful.Spec/spec-ledger.json`):** Maps every capability ID (`SPEC-M2-001` .. `SPEC-M6-015`) to `(TargetPr, DocAnchor, Mode: runtime | compile-pass | compile-fail | aot-http, Status: pending | active)`.
- **Compile-outcome fixtures (`tests/Resultful.Spec/Fixtures/*.cs`):** Real consumer `.cs` files carrying inline markers (`// expect-error: RFG0001`, `// expect-warning: CS8509`, `// expect-clean`, `// expect-fix: RF0001`). A Roslyn runner (`eng/verify-spec.cs` or `SpecFixtureRunner.cs`) executes every `active` fixture and fails if a PR claims to close a capability without flipping its ledger entry to `active` and passing.
- **Reference consumer app (`tests/Resultful.E2E.App/`):** A realistic 3-project consumer solution (`E2E.Domain`, `E2E.Api`, `E2E.Client`) modelling the 3 flagship *Errors in practice* flows (place order with closed `OrderError` + `PlaceOrderResult`, sign-up with `RegisterResult` `ManyErrors`, layered `DbTimeout` with `WithContext` + `TryFind` retry). At PR `01b`, each project compiles with `#if SPEC_PR_XX` guards; each milestone PR removes its `#if` guard and activates its HTTP/runtime assertions.
- **PR-level progress gate:** Every PR `k` must flip at least its declared `SPEC-*` IDs from `pending` to `active` in `spec-ledger.json`. The CI gate fails if any PR <= `k` still has `pending` specs or if an `active` spec is skipped.

## 2. The End-to-End Oracle

Per-PR unit tests prove local invariants; two independent oracles prove the whole product works as shipped:

1. **Packed-NuGet NativeAOT HTTP Oracle (`tests/Resultful.E2E.App/` + `eng/verify-e2e.cs`):**
   - Packs **all** `src/*/*.csproj` into `temp/stack/feed/` (zero `<ProjectReference>` in `E2E.Domain`/`E2E.Api` — only `<PackageReference>` from the local feed, proving NuGet dependency groups, analyzer/generator `analyzers/dotnet/cs` layout, and `netstandard2.0` + `net10.0`/`net11.0` TFMs all resolve cleanly).
   - Runs `dotnet publish tests/Resultful.E2E.App/E2E.Api -c Release -r <rid> /p:PublishAot=true` with `TreatWarningsAsErrors=true` (catches any `IL2xxx`/`IL3xxx` trim/AOT regression across core + generator + `Resultful.AspNetCore` + `Resultful.Text.Json`).
   - Starts the published native binary on an ephemeral port and runs HTTP integration tests against real sockets: `200 OK`, `400 ValidationProblemDetails` (RFC 9457 with field map from `ManyErrors`/`ValidationFailed`), `404`/`409` from `[ClosedHierarchy]` switch and `TryFind` under `WithContext`, plus OpenAPI schema check (unverified: exact .NET 11 Minimal API OpenAPI package version in local cache).
2. **Differential Reference Model (`tests/Resultful.Properties.Tests/ReferenceModelTests.cs`):**
   - Implement a trivial 40-line pure reference model (`RefResult<T,E>`, `RefValidation<T,E>`) in tests using plain discriminated records and `List<E>`.
   - Drive **CsCheck** randomly generated sequences of `Map`, `Bind`, `Tee`, `OrElse`, `Plus`/`Combine`, `WithContext`, `ToResult`, `AsResult`, and generated `[ResultShape]` round-trips against *both* Resultful and the reference model, asserting identical outcomes, error ordering, and zero unexpected exceptions.
   - Differential testing catches subtle state-byte / `default` / short-circuit / accumulation bugs that example tests miss.

## 3. Proving Generator & Analyzers Across TFMs, Assemblies, and Packages

In-memory `CSharpCompilation` tests are necessary for speed, but **insufficient** to prove real MSBuild/NuGet behaviour. Enforce a 3-layer matrix:

1. **In-memory fast suite (`tests/Resultful.Generators.Tests`, `tests/Resultful.Analyzers.Tests`):**
   - Run on both `net10.0` (asserting **zero** `[Union]` emission, `Match`/`Switch` fallback) and `net11.0` (asserting `[Union]` flat + grouped exhaustive `switch`, `CS8509` on removed arm, `CS8509` suppressor with `null`-arm guard).
   - Incremental step-tracking (`Resultful.BaseIndex`, `Resultful.Hierarchies`, `Resultful.Shapes`, `Resultful.UnionAvailable`, `Resultful.Emit`) asserting `Cached`/`Unchanged` on unrelated edits and `Modified` only on affected hierarchies.
   - Reflection check: zero `ISymbol`/`SyntaxNode`/`Location`/`Compilation` fields in cached models.
   - Structural + IL budget gate (`src/Resultful.Generators/api/emitted-api-budgets.txt`): `MEMBERS`, `PARAMETERS`, Release IL bytes via `System.Reflection.Metadata`, zero `box` outside `get_Value`.
2. **Real MSBuild bare-compile pass (`analysis/GeneratedCode/` + `eng/verify-generated.cs`):**
   - Emit `.g.cs` from all 9 fixtures (including 4-level deep, concrete intermediate, colliding names, nested, interface `Success`, cross-assembly) and compile them in a real MSBuild project with `ImplicitUsings=disable`, `Nullable=enable`, `IsAotCompatible=true`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, and `generated_code = false` in `.editorconfig`.
   - **Positive control:** Include one deliberate unused variable in a test `.g.cs` to prove `generated_code = false` actually triggers NetAnalyzers on files starting with `// <auto-generated/>`; if Roslyn suppresses it anyway, `eng/verify-generated.cs` strips the header during the bare compile and documents that fact.
3. **Packed cross-assembly NuGet smoke (`tests/Resultful.E2E.App`):**
   - `E2E.Domain` (references packed `Resultful` + `Resultful.Generators` + `Resultful.Analyzers`) declares `[ClosedHierarchy] OrderError`.
   - `E2E.Api` (references `E2E.Domain` as a project/assembly + packed packages) declares `[ResultShape(typeof(OrderError), Success = typeof(Order))]` and compiles an `Evil : OrderError` negative fixture expecting the cross-assembly analyzer error.
   - Proves the generator/analyzer DLLs actually load from `analyzers/dotnet/cs/` inside the `.nupkg` on both `net10.0` and `net11.0` (catches `RS1038`, missing `netstandard2.0` polyfills, or broken `.props`/`.nuspec` layout).

## 4. Which Extra Gates Earn Their Place — and When

| Gate | Verdict | Where / When | Concrete Tooling |
|---|---|---|---|
| **API-coverage gate** | **Keep & tighten (PR 01/02)** | Every PR in CI | Fix #36's `PublicApiCoverageTests.cs`: drop `CandidateSymbols`, require both success and failure path invocations per member (or coverlet line/branch ratchet >= 90% on `src/Resultful`). |
| **Compiled doc snippets** | **High ROI — add at PR 13 & 25** | M4 + M6 | `eng/verify-docs.cs`: extracts fenced ` ```csharp ` blocks tagged `// compile` or inside `docs/errors-in-practice.md` and `README.md`, compiles them against the real packed assemblies on `net11.0`. Stops docs drifting from code. |
| **Benchmark / allocation gate** | **Targeted — PR 14 & PR 24** | M4 (shape alloc) & M6 (union) | Don't run full BenchmarkDotNet in every PR CI (noisy on shared runners). Use deterministic `GC.GetAllocatedBytesForCurrentThread()` zero-allocation assertions in `tests/Resultful.Generators.Tests` / `Resultful.Properties.Tests` on every PR; run BenchmarkDotNet (`[MemoryDiagnoser]`) once in PR 14 (interface-box vs forwarded `Map`/`Bind`) and PR 24 (`net10` vs `net11` `[Union]`) and check numbers into a decision doc. |
| **Mutation testing (Stryker)** | **Defer to post-stack / M7** | Nightly / manual only | `dotnet-stryker` (unverified: whether installed locally or on .NET 11) is far too slow for a 24-PR autonomous stack and noisy on `netstandard2.0` multi-targeting. Keep it out of the stack loop; differential CsCheck + branch-coverage ratchet gives 90% of the value at 5% of the cost. |

## 5. Keeping the Agent Pipeline Correct Over Hours

A 24-PR autonomous run fails primarily from **self-grading bias**, **drift across compactions**, and **rebase storms**. Guard against all three:

1. **Three-role separation per PR:**
   - **Implementer subagent** (fresh worktree `temp/stack/wt/pr-NN`): reads `GOAL.md`, the relevant decisions, and the PR's `SPEC-*` items; writes code + tests; runs lightweight local build (`nice dotnet build -m:2` on affected project only).
   - **Independent Reviewer subagent** (read-only, fresh context): receives *only* `git diff <base>..HEAD`, the relevant decision doc, and `GOAL.md`. Checks API names, `PublicAPI.Unshipped.txt`, doc claims, failure paths, and `DECISIONS-TAKEN.md`. Never shares the implementer's reasoning trace.
   - **Adversarial Verifier subagent** (runs at milestone boundaries: after PR `04`, `12`, `17`, `22`, `24`): tries to **break** the milestone — writes edge-case inputs (`default`, `null` successes, overlapping `Success`, deep 5-level hierarchies, cross-assembly `Evil` subclasses, `ImplicitUsings=disable`, packed `.nupkg` restore with `--no-cache`) and files concrete bugs before the stack moves past the milestone.
2. **Orchestrator verification rules (trust nothing unmeasured):**
   - Never accept "tests pass" from a subagent's summary alone. Before marking a PR `green` in `PROGRESS.md`, the orchestrator verifies directly via `gh pr checks <PR>` (CI `build` + `lint` green), inspects `spec-ledger.json` diff (expected `SPEC-*` flipped to `active`), and confirms `PublicAPI.Unshipped.txt` has no unintended breaks.
   - **Checkpoint files:** Every PR appends one structured line to `temp/stack/PROGRESS.md` (commit SHA, PR #, CI run URL, `SPEC-*` closed) so orchestrator compaction never loses state.
3. **Parallelism & restacking discipline:**
   - **Freeze foundation (`01`–`04`) strictly serially** — zero parallelism until `04` is green in CI.
   - Once `04` is green, **spec/review/adversarial prep** for PR `k+1` can run in parallel with CI for PR `k`, and independent satellites (`20a`/`21`/`22`) can be drafted in parallel worktrees against `19`'s tip, then stacked linearly (`git rebase --onto`) in one clean pass.
   - **Restacking rule:** If a reviewer/verifier forces a fix on PR `j` while `j+1..k` exist, fix `j`, wait for `j`'s CI green, then run one deterministic rebase loop (`git rebase --onto` + `--force-with-lease`) up to `k` — never amend two layers at once.
   - **Hard stop:** If the same PR fails CI twice for an unclear reason, or if a rebase produces >3 non-trivial conflict files, stop and escalate to the orchestrator rather than letting a subagent guess.

## 6. Recommended Reslicing of the Stack

Keep `GOAL.md`'s 25-PR structure, with **three targeted adjustments** to wire the executable spec and E2E oracle in cleanly:

1. **Insert `01b: executable-spec`** right after `01`: lands `tests/Resultful.Spec/spec-ledger.json`, compile-outcome fixtures, `tests/Resultful.E2E.App/` skeleton, and `eng/verify-spec.cs`. Gives every subsequent PR (`05`–`24`) a concrete checklist of specs to turn green.
2. **Split `13` (`generator`) into `13a` and `13b`**:
   - `13a`: rebase #37 onto real core, delete stubs, enable analyzer NuGet packaging (`DevelopmentDependency`), pass package check.
   - `13b`: real MSBuild bare-compile project (`analysis/GeneratedCode/` with `generated_code=false`, `ImplicitUsings=disable`, `IsAotCompatible=true`) + positive-control check on `// <auto-generated/>` + tightened Release IL budgets.
3. **Add `23c: e2e-aot-oracle`** (or make it the gate of `23a` + `22`): packs all `src/*` packages to a local feed, builds and publishes `tests/Resultful.E2E.App/E2E.Api` with `PublishAot=true`, boots the native binary, and runs the full HTTP + OpenAPI + cross-assembly analyzer/generator verification end to end.
4. **Add `eng/verify-docs.cs` in `25` (`docs-sync`)**: compiles every marked snippet in `docs/errors-in-practice.md` and `README.md` against the final packed packages so the entire stack closes with 100% compiled documentation.

## 7. Addendum: 0.2.0 Release Gate (`04b`) & Multi-Roslyn / Multi-TFM Verification

### Proving `0.2.0` (`04b`) is safe to ship
Before Ryan merges `00`–`04b` and cuts `release/0.2`, `04b` passes five concrete gates:
1. **Zero-break API & binary compatibility:** `PublicAPI.Unshipped.txt` is empty (all entries in `PublicAPI.Shipped.txt`, zero removals vs `0.2.0-beta06`). Verify binary compatibility against the `0.2.0-beta06` `.nupkg` across `netstandard2.0`, `net10.0`, and `net11.0` using `Microsoft.DotNet.ApiCompat.Tool` (unverified: whether installed in CI) or `PublicAPI` diff.
2. **Package shape:** `eng/verify-package.cs` passes on `Resultful.0.2.0.nupkg` + `.snupkg` (no prerelease suffix, no `netstandard1.x`/`.NETFramework` groups, `OneOf >= 3.0.271`, SourceLink + README + MIT license).
3. **Packed consumption across all 3 TFMs:** `tests/Resultful.Consumption` runs with `-p:UsePackedPackage=true` (version read dynamically from `src/Resultful/Resultful.csproj`) on `net10.0` and `net11.0`, with `NetStandardLoadTests` loading `lib/netstandard2.0/Resultful.dll` **from the unpacked `.nupkg`**, not `src/bin/`.
4. **Clean restore & Paket `strategy: min`:** A scratch consumer (`temp/stack/paket-0.2/`) restores `Resultful 0.2.0` with `--no-cache` and via Paket `strategy: min`, asserting `OneOf 3.0.271` resolves with zero `NETStandard.Library` or `NU1903` warnings.
5. **Clean handoff to 0.3:** After Ryan merges `00`–`04b`, tags `Resultful/v0.2.0`, and branches `release/0.2`, PR `05` rebases onto `main` and bumps `src/Resultful/Resultful.csproj` to `0.3.0-beta01`.

### Verifying conditional per-TFM dependencies & multi-Roslyn analyzer builds (`13`, `16a`–`17`, `20a`–`22`)
1. **Static `.nupkg` inspection in `eng/verify-package.cs`:**
   - **Satellites (`20a`–`22`):** Parse `.nuspec` `<dependencies>` per group and assert `net10.0` pins `10.x` (`Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`) and `net11.0` pins `11.x` — never `11.x` leaking into `net10.0`.
   - **Generators & Analyzers (`13`, `16a`):** Assert both `analyzers/dotnet/roslyn4.14/cs/*.dll` and `analyzers/dotnet/roslyn5.x/cs/*.dll` exist (exact `roslyn5.x` folder name — e.g. `roslyn5.0` vs `roslyn5.9` — is **unverified** until probed against SDK 11's `csc` version in PR 13), zero `lib/` DLLs, and inspect each DLL's `AssemblyReferences` via `System.Reflection.Metadata` to prove `roslyn4.14` references `Microsoft.CodeAnalysis <= 4.14.0` and `roslyn5.x` references `5.x`.
2. **2×2 Compiler × Target smoke matrix in CI (packed `.nupkg`s only, no `<ProjectReference>`):**
   - **Job A — SDK 10.0.x compiler → `net10.0` target** (isolated `global.json` in `temp/stack/sdk10-smoke/`): builds a consumer referencing packed `Resultful.Generators` + `Resultful.Analyzers` with `TreatWarningsAsErrors=true` and `EmitCompilerGeneratedFiles=true`. Asserts zero `CS8032`/`CS9057` host-mismatch warnings, `roslyn4.14` loaded, zero `[Union]` members emitted, and `Match`/`Switch` + analyzer rules pass.
   - **Job B — SDK 11.0.100 compiler → `net10.0` AND `net11.0` targets:**
     - On **`net10.0` target**: proves the critical edge case — SDK 11 selects `roslyn5.x/cs/`, so the `roslyn5.x` generator **must** still gate `[Union]` emission on `Resultful.UnionAvailable` (`GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute") != null`), NOT on `#if ROSLYN_5_0_OR_GREATER`, emitting zero `[Union]` on `net10.0`.
     - On **`net11.0` target**: asserts `roslyn5.x/cs/` loads cleanly, `[Union]` is emitted, flat + grouped `switch` compiles with zero `CS8509`, and removing an arm fails with `CS8509`.
