# Review of the stack plan: write the target first, then turn it green

Read: `GOAL.md` (as updated: 04b, per-Roslyn builds, 16a/b, 20a/b, 23a/b, 25), the roadmap, architecture,
decisions 3, 5, 6, 10, 11, 14, 15, 16, both testing explorations, *Errors in practice*, the core, #36 and #37.
Nothing was built or run for this review. PR numbers below are the updated `GOAL.md` numbers.

## Verdict

The plan is a sound build order, not yet a definition of done. "Green" means `build` and `lint` pass, and
each PR writes its own tests, so each PR marks its own homework. The end state exists only as prose. Proposal:
1. Prove `0.2.0` is the same library as `0.2.0-beta06`, by binary, before it ships (04b).
2. Put the whole 0.3 target above 04b as code that compiles. Every later PR turns named items green.
3. Test what a consumer gets: only the nupkgs, under four SDK bands, over HTTP, as a native-AOT binary.

## Where the current plan is weak

The update fixed test discovery, the `result-error` row, the shape interfaces and docs in 25. These remain.

1. **Done is "CI is green".** Rule 3 has no measure of how much of the target a PR delivered.
2. **The design-target tests are empty.** #36's `TargetDesignSpecTests.cs` has six `[Fact(Skip = "Pending M2…")]`
   methods whose bodies are one comment. The active ones compile against a string stand-in, so they pass without Resultful.
3. **Two stub cores, both wrong in the same place.** `StandInCore` (#36) and `CoreStubSource` (#37) make `Unit`
   a struct. The shipped `Unit` is a class.
4. **The API gate can't see new API.** `PublicApiCoverageTests` reads `PublicAPI.Shipped.txt` only. Rule 8 puts
   every new member in `Unshipped`. Nothing the stack adds after 04b is covered.
5. **The packed-package run can test stale bits.** Reading the version from the csproj doesn't fix it. The
   version is the same on every run, and `actions/cache` restores `NUGET_PACKAGES`. A cached extract wins over
   the fresh nupkg. Once `0.2.0` is on nuget.org, a run may test the published one.
6. **04b has no proof of "safe".** Its gate is the package check and a Paket restore. But 03 edits the core's
   source (nullable audit, XML docs, analysis fixes) just before the release. Nothing compares the result
   with beta06.
7. **08 rewrites the characterisation tests it should be judged by.** "Updated to show the change" lets the
   implementer of the storage rewrite decide what changed.
8. **Proof arrives late.** The generator is 13, its behaviour tests are 14, the samples are 23a. Eight core
   PRs (05 to 12) land before anything shows a shape can stand on them.
9. **The second Roslyn folder is a trap.** Read from `csc -version` on this machine: SDK 9.0.315 is Roslyn
   4.14.0, SDK 10.0.401 is 5.9.0, SDK 11.0.100 is 5.12.0. So SDK 10 loads a `roslyn5.x` build too. "5.x emits
   `[Union]`" can't be a build-time fact. And no CI leg uses a 4.14 compiler, so the baseline build is untested.
10. **Decision 3's checks gate `[Union]` on shapes too.** #37 emits `[Union]` members now. The matrix is in 24.
11. **Still unassigned:** the `.editorconfig` list for third-party types (decision 15 item 3), the rule for
    result fields a constructor never assigns (decision 10 layer 3), the rule that suggests a
    `private protected` constructor, and the OpenAPI check in M5's "Done when".
12. **The orchestrator trusts summaries.** Nothing says what it checks itself, or what stops an implementer
    weakening a test, adding a `NoWarn` or skipping a case to get green.

## 1. Proving 0.2.0 is safe to ship (04b)

The claim to prove: a consumer of `0.2.0-beta06` can move to `0.2.0` and see no change. Five checks, all in a
new CI job, `release-check`. The baseline is the beta06 nupkg: from nuget.org if it is published (unverified),
otherwise packed from `main` and kept as a CI artifact.

| Check | How | Fails when |
|---|---|---|
| Same behaviour | The characterisation suite (01, 02) runs three ways: project, packed `0.2.0`, baseline nupkg. One suite, three passes. | Any test differs between baseline and `0.2.0` |
| Binary compatible | Build the suite against the baseline. Copy the new `Resultful.dll` over it without rebuilding. Run. Per target: `netstandard2.0`, `net10.0`, `net11.0`. | `MissingMethodException`, `TypeLoadException` |
| Same public API | `eng/api-diff.cs`: no member added or removed. Nullable re-annotations from 03's audit are listed in `eng/api-diff-0.2.0.txt`. Also `EnablePackageValidation` with `PackageValidationBaselineVersion` (unverified against a pre-release baseline). | A member is added or removed, or the list differs from the committed one |
| Same code | `eng/il-diff.cs` reads both DLLs with `System.Reflection.Metadata` and lists every method whose IL changed, in `eng/il-diff-0.2.0.txt`. | The list differs from the committed one |
| Same package | `eng/verify-package.cs`, plus a diff of the nuspec's dependency groups and the file list against the baseline. Only the version may differ. | Any other difference |

Consumers, each restoring only the packed nupkg into a clean packages folder:
- NuGet on `net8.0` (the `netstandard2.0` asset), `net10.0`, `net11.0`, with `NuGetAudit` on and NU1903 an error.
- Paket `strategy: min`, with `temp/paket-beta06` as the starting point.
- `net481` on one `windows-latest` job. This is the only run of the `netstandard2.0` asset off .NET Core.
- Optional: `FluentAssertions.Resultful`'s tests against the packed `0.2.0` (track A1, the other repository).

Both lists are reviewed files. Both scripts land in 02, so 03 is gated by them and should leave the IL list
empty. After the merge: `release/0.2` keeps `release-check` as its gate, with `0.2.0` as the new baseline. On `main`,
the 0.2 characterisation suite is frozen. It becomes the baseline side of the differential in section 3.

## 2. The 0.3 target as an executable specification

Three artefacts, in two new PRs above the release: **04c `spec-harness`** and **04d `reference-app`**. They
aren't part of 0.2.0. The maintainer reviews the target there, once.

**A. The target API, as code.** `tests/Resultful.TargetApi/<Package>/*.cs`: every public member of every
package in the end state, with `throw null` bodies, as dotnet/runtime does for reference assemblies.
- One project per package, same assembly name. PublicApiAnalyzers 5.6.0 keeps each one's `PublicAPI.Shipped.txt`
  in step with the stubs. That file is the target list.
- It replaces both stubs. Fixtures, the generator's output tests and the reference app compile against it.
- The gate on every PR: the real `PublicAPI.*.txt` is a subset of the target. At 25 they are equal.
- Overload clashes show up in hour one. It forces every open question closed (section 8).

**B. Capability fixtures.** `tests/Resultful.Spec/Fixtures/<ID>/*.cs`, compiled and run by one harness,
`SpecHarness.cs` (xunit.v3 4.0.2, merged from #36's `CompilationHarness` and #37's `GeneratorTestHarness`).
- IDs extend the `SHAPE-*` table: `CORE-*`, `ZERO-*`, `ERR-*`, `VAL-*`, `OP-*`, `ASYNC-*`, `HIER-*`, `GEN-*`,
  `RF-*` (one per analyzer rule), `UNION-*`, `PKG-*`, `HTTP-*`, `AOT-*`, `MIG-*`, and `UC01` to `UC10`.
- A compile fixture carries `// expect: CS8509` markers, checked both ways.
- A run fixture has static `[SpecCase]` methods. The harness compiles it against the built `Resultful.dll`,
  runs the generator and analyzers through the driver, loads the result and calls each case.
- A fixture for a missing member can't sit in a project that builds. Here it is a recorded failing compile.
- Decision 3's matrix is here from the start: `Result<string, string>`, a `List<string>` success, a null
  success, the zero value, from a second assembly.

**C. The ledger.** `tests/Resultful.Spec/ledger.tsv`: ID, state, owning PR, SHA-256 of the fixture files.
- `pending` is strict. A pending item that passes fails the build, with "flip this line".
- `done` items pass on `net10.0` and `net11.0`.
- The job writes `spec-report.json`. The run is finished when no line is `pending`.

**Protecting the spec.** An implementer changes only the `state` column. A real change is a separate `spec:`
commit, a line in `DECISIONS-TAKEN.md` and a section in the PR body. More than three in one PR stops the run.

## 3. The end-to-end oracle

**The reference application**, written in 04d against the target API:
- `samples/Resultful.Shop.Domain`: closed hierarchies and shapes. A separate assembly on purpose.
- `samples/Resultful.Shop.Api`: orders (UC1), sign-up (UC2, UC3), a payments adapter (UC5), context (UC4).
- `samples/Resultful.Shop.Worker`: the queue handler over an in-memory channel (UC6).
- `samples/Resultful.Shop.Legacy`: UC10's "before" code. A library with an `internal` `[MustObserve]` (UC9).
  Each use case is a folder, built only when its `UCnn` line is `done`. 23a's samples become this app.

**`tests/Resultful.EndToEnd`** drives it, with no `ProjectReference`.
- Pack every package with `-p:VersionSuffix=ci.$GITHUB_RUN_ID`. Restore with `--packages temp/e2e-packages`
  and package source mapping that sends `Resultful*` to `artifacts/` only. This fixes weakness 5.
- Build with `TreatWarningsAsErrors`, `AnalysisLevel latest-all` and nullable on. Generated code must be clean.
- `http-cases.json`: one request per error leaf, asserting the status and the ProblemDetails body. The OpenAPI
  document lists the success and each failure status.
- `PublishAot=true` on `net11.0`. The published binary answers the same HTTP cases. IL2xxx or IL3xxx fails.

**Negative consumers.** `tests/Resultful.EndToEnd/Broken/*`: projects that must not build from the packages.
A missing arm, a discarded result, a `default` result, a type derived outside its base's assembly, a bare
string into `Result<string>`. Each sets `<ErrorLog>`. The runner compares the SARIF with the expected IDs,
both ways. Only this proves the analyzers and the suppressor work from the nupkg under real MSBuild.

**A reference model.** `tests/Resultful.Properties.Tests/Model.cs`: a twenty-line class-based result that is
plainly correct. CsCheck 5.0.1 generates random programs (`Map`, `Bind`, `Then`, `OrElse`, `Combine`,
`WithContext`, …) and runs each on the model, the core, the async overloads and a shape. All must agree.

**A baseline differential for 08.** The same random programs run on `0.2.0` (packed as `Resultful.Baseline`)
and on the new storage. Every difference must be on a committed allowlist. That list is the break, as data,
and it answers weakness 7.

## 4. The generator and analyzers: per Roslyn build, per compiler

**In process.** One test source tree, two projects: `Resultful.Generators.Tests.Roslyn414` and `.Roslyn5`.
Each references its own generator build and its own `Microsoft.CodeAnalysis.CSharp`. Same for the analyzers.
- Both builds give byte-identical output for every fixture with no `[Union]`. The gate is a diff.
- The 4.14 build never emits `[Union]`. The 5.x build decides at run time, from the compilation: the attribute
  exists and the language version allows it. Fixtures cover each combination.
- CsCheck builds random hierarchies (depth, nesting, generics, accessibility, name clashes). The result is a
  clean compile or an `RFG` diagnostic, never CS8785. A run with the syntax trees shuffled gives the same bytes.
- Seeded faults: the harness rewrites generated source (drop the zero guard, swap two arms, return the base
  for a leaf). Every fault must fail a fixture. This shows the tests can fail.

**The package.** `eng/verify-package.cs` gains analyzer checks:
- `analyzers/dotnet/roslyn<v>/cs/<name>.dll` exists for each build. No `lib/`. `developmentDependency` is set.
- Each DLL's `Microsoft.CodeAnalysis` assembly reference is no newer than its folder's version.
- For satellites: each dependency group holds only versions for its own target (no 11.x in `net10.0`).

**Under each compiler.** The end-to-end job runs the Shop and the broken consumers in four legs.

| Leg (pinned band) | Compiler | Target | Must load | Must hold |
|---|---|---|---|---|
| SDK 9.0.3xx | Roslyn 4.14.0 | `net8.0` | the `roslyn4.14` build | `Match` path works. No `[Union]`. |
| SDK 10.0.1xx | unverified, expected 5.0 | `net10.0` | asserted | No `[Union]`. No CS9057. |
| SDK 10.0.4xx | Roslyn 5.9.0 | `net10.0` | the 5.x build | No `[Union]`: the target has no attribute. |
| SDK 11.0.1xx | Roslyn 5.12.0 | `net11.0`, `net10.0` | the 5.x build | `[Union]` on `net11.0` only. Flat `switch` is exhaustive. |

- The SDK 9 leg can't target `net10.0`, so it builds `Shop.Domain`, a console driver and the broken consumers.
  The other legs build the whole Shop.
- Each leg has its own `global.json`, treats NU1605 and NU1608 as errors, and asserts which DLL loaded: read
  the `Analyzer` items (`dotnet msbuild -getItem:Analyzer`, or the binlog) and compare the folder with the table.
- CS9057 (analyzer built for a newer compiler), CS8032 (analyzer can't be created), CS8785 (generator threw)
  and AD0001 fail the leg. They are warnings by default, so the leg sets them as errors by ID.
- The 5.x build must compile against the lowest 5.x it supports, or SDK 10.0.1xx reports CS9057. The tests
  pin Roslyn 5.9.0 today, which matches SDK 10.0.4xx, not SDK 11. Add a 5.12.0 test project for `[Union]`.

**Analyzers.** One `RF-*` fixture set per rule: positive, negative, a user's own type, an `internal` attribute
copy. Code fixes use `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` (unverified on Roslyn 5.x) with fix-all.
The suppressor needs its own harness with suppressed diagnostics reported: suppressed with all arms, not with
one missing, not without `null` on a nullable input.

## 5. Other gates: which earn a place

| Gate | Verdict | From |
|---|---|---|
| API coverage: every member in `Shipped` and `Unshipped`, every package, is called by a fixture or a law | Yes, every PR | 01 |
| Counts: tests per project never fall, skips never rise, against `eng/test-counts.json` | Yes | 02 |
| Suppressions: new `NoWarn`, `#pragma warning disable`, `Skip =`, `continue-on-error` | Yes, as a diff the PR body lists | 02 |
| Doc snippets: tagged `csharp` blocks in the README and *Errors in practice* equal regions of the Shop | Yes. The docs are the spec. `eng/check-snippets.cs`. | 04d, strict at 25 |
| Allocations, struct size (`Unsafe.SizeOf`), IL budgets | Yes, as tests | 08, 13 |
| Release plan: `eng/release-plan.cs` against made-up change lists | Yes. Nothing publishes, so 04 has no other test. | 04 |
| Mutation testing, Stryker.NET | Not per PR. Manual `mutation.yml` runs at checkpoints. Survivors go in the PR body. No threshold. | 04b, 19, 17, 25 (stack order) |
| Branch coverage ratchet | Keep, core only. The weakest of these. | 02 |
| BenchmarkDotNet | Not a time gate: shared runners are noisy. Gate only "Allocated = 0". One manual run feeds 24's decision. | 13, 24 |

Stryker replaces a string literal whole, so it can't mutate emitted source usefully. Seeded faults cover that.

## 6. The agent pipeline

**Roles.** Each is a fresh subagent, except the orchestrator.
- **Orchestrator.** Writes no product code. Owns `PROGRESS.md`, the contracts and every restack.
- **Implementer.** One per PR, in `temp/stack/wt/NN`. Gets `temp/stack/contracts/NN.md`: the IDs to flip, the
  paths it may touch, the gate commands. Returns `temp/stack/reports/NN.json`, a list of claims.
- **Reviewer.** Gets the contract, the decisions and `gh pr diff`. Never the implementer's summary.
- **Adversary.** For 03, 04b, 08, 09, 13, 15, 16a, 16b and 17. It writes a fixture the spec says should pass
  and that fails. For 03 and 04b it hunts for any difference from beta06. A valid find becomes a fixture.

**What the orchestrator runs itself, per PR.** It never accepts "tests pass".
1. `gh pr checks <n> --json name,bucket`: every job passes.
2. `gh run download` for `spec-report.json`. The IDs flipped in the ledger, the IDs green in the report and
   the IDs in the contract are the same set.
3. `git diff --name-only <prev>..<head> -- tests/Resultful.Spec/Fixtures tests/Resultful.TargetApi` is empty,
   and the ledger's hashes match. `eng/api-diff.cs` shows no member outside the target.
4. The counts and suppressions gates.
5. Size: over about 800 changed lines outside tests means split the PR.
6. `git range-diff` after a restack: no PR's content changed except where a conflict was resolved.

**Parallel and in sequence.** The target API lets satellites and analyzers compile before the core is real.
- At most two implementers at once, because the machine is shared.
- Lane A, in sequence: 01 to 04b, then the core, 05 to 12.
- Lane B, alongside: 04c and 04d while lane A is on 02 to 04. Then 16a, 16b and 20a against the target API.
- Lane B's work is rebased into its stack position when lane A reaches it. Its run fixtures flip then.

**Restacking.**
- A reviewed, green PR is frozen. A later fix is a `fix:` PR at the top, unless it changes API or the spec.
- Fixes that must go low are batched at checkpoints: after 04b, 12, 17 and 23b.
- After the maintainer merges 00 to 04b: one rebase onto `main` with `git rebase --update-refs`, then go on.

**Stop conditions to add.**
- A `release-check` difference that isn't on a reviewed list. The pending count rises. A `done` item goes red.
- A reviewer rejects the same PR three times, or a restack conflict isn't mechanical.
- A PR takes more than 90 minutes. Mark it `blocked` and carry on with the other lane.

## 7. Changes to the stack

| # | Change | Gate beyond `build` and `lint` |
|---|---|---|
| 01, 02 | Add the API gate fix, the per-run package version, the model, counts and suppressions gates | Every shipped member is called. The packed run can't see a cached package. |
| 03 | Core edits limited to annotations and docs | The IL list is empty. Each re-annotation is on the API list. |
| 04b | Add `release-check` (section 1), the `net481` and `net8.0` consumers, mutation run 0 | All five checks pass. The IL and API lists are in the PR body. |
| 04c, 04d | New: `spec-harness`, `reference-app` | The target API and the Shop compile. Every fixture is strict `pending`. Each gate has a self-test that shows it can fail. |
| 05–07, 09, 10 | As planned | Their `CORE-*`, `ERR-*`, `VAL-*` lines flip. The model agrees. |
| 08 | Split: 08a storage with no behaviour change, 08b the zero-value guard | 08a: the baseline differential is empty. 08b: its allowlist holds only the zero value. `ZERO-*` flip. |
| 11, 12 | As planned | `PKG-*`, `MIG-*` flip. The Legacy app fails with exactly the listed errors. |
| 18, 19 | Move up to follow 12, so the core is finished before tooling | `OP-*`, `ASYNC-*` flip. **Checkpoint: the core equals its target API. Mutation run 1.** |
| 13 | Split: 13a #37 against the target API, placed after 04d, not packed. 13b the real core, both Roslyn builds, the package. | 13a: fuzz and determinism pass, the Shop's shapes compile. 13b: the four compiler legs pass. |
| 14 | Dissolves. Its harness is 04c. Its tests are fixtures that 13b flips. | `SHAPE-*`, `HIER-*`, `GEN-*`, shape `UNION-*` flip. Seeded faults are all caught. |
| 15–17 | As planned, plus the unassigned rules in weakness 11 | `RF-*` flip. The broken consumers fail as listed, from the packages, on all four legs. **Mutation run 2.** |
| 20a–22 | As planned | `HTTP-*`, `AOT-*` flip. The AOT binary passes the HTTP cases. |
| 23a, 23b | 23a becomes "finish the Shop". 23b as planned. | UC01 to UC10 are `done`. |
| 24 | As planned | The core's `UNION-*` flip. The numbers are in a new decision. |
| 25 | Add the close-out | No `pending` line. Every package equals its target. The snippet gate is strict. Mutation run 3. |

## 8. What is missing before this is "fully defined"

04c can't be written until these are answered. Take a default for each and log it.
- Decision 14's open questions that change signatures: `Unit` as class or struct (12 decides it, which is too
  late), how `Ok()` reaches `Result<Unit, TError>`, `Bind` and `+` on `Validation`, `Context` in equality.
- Decision 16: async on a shape, `From` mappings, JSON for a shape, whether `Map` and `Bind` stay forwarded.
- The analyzer rule table: IDs, severities, which have fixes, the `.editorconfig` keys.
- The HTTP contract: the status for each error kind, the ProblemDetails fields, the validation body.
- What `Resultful.Diagnostics` logs. Equality, `GetHashCode` and `ToString` for every result type.
- The support statement: which SDKs can load the generator, the lowest Roslyn, and that IDE hosts are untested.
- The 0.2.x policy: what may go to `release/0.2`, and whether 0.2.x gets `[Obsolete]` pointers to 0.3.
- A version for each package and the 0.3.0 migration guide. The Legacy app's error list is its first draft.

## 9. Unverified, and cost

- Cost: two PRs of pure specification, and CI grows to about seven jobs. Keep `build` and `lint` required.

- Whether `0.2.0-beta06` is on nuget.org, and package validation against a pre-release baseline.
- The Roslyn version in SDK 10.0.1xx, and which folder each compiler picks. The legs assert it.
- `dotnet msbuild -getItem:Analyzer` returning the resolved analyzers. The binlog is the fallback.
- Stryker.NET with Microsoft.Testing.Platform and xunit.v3 4.0.2. `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing`
  on Roslyn 5.x. BenchmarkDotNet on `net11.0`. Native AOT on `ubuntu-latest` with no extra setup.
- The final shape of `[Union]` beyond what the docs mark **verified** on SDK 11.0.100.
