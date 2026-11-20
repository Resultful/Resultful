# How the stack is verified

Merged from agy's and Fable's reviews (`agy-review.md`, `fable-review.md`). `GOAL.md` says which PR owns each
piece; this file says how it works.

## 1. `release-check`: 0.2.0 is the same library as beta06 (scripts in 02, gate in 03 and 04b)

The baseline is `Resultful 0.2.0-beta06`, packed in CI from the `0.2.0-beta06` tag and kept as an artifact.

| Check | How | Fails when |
|---|---|---|
| Same behaviour | The characterisation suite (01, 02) runs against the project, the packed `0.2.0` and the baseline. | Any result differs between baseline and `0.2.0`. |
| Binary compatible | Build the suite against the baseline, copy the new `Resultful.dll` over it without rebuilding, run. Per target: `netstandard2.0`, `net10.0`, `net11.0`. | `MissingMethodException`, `TypeLoadException`. |
| Same public API | `eng/api-diff.cs`. Nullable re-annotations from 03 are listed in `eng/api-diff-0.2.0.txt`. Also try `EnablePackageValidation` with a baseline version. | A member is added or removed, or the list differs from the committed one. |
| Same code | `eng/il-diff.cs` lists every method whose IL changed (`System.Reflection.Metadata`) into `eng/il-diff-0.2.0.txt`. | The list differs from the committed one. |
| Same package | `eng/verify-package.cs`, plus a diff of the nuspec dependency groups and file list. | Anything but the version differs. |

Consumers restore only the packed nupkg into a clean packages folder:
- NuGet on `net8.0` (the `netstandard2.0` asset), `net10.0`, `net11.0`, with `NuGetAudit` on and NU1903 an error.
- Paket `strategy: min`, starting from `temp/paket-beta06`, with `--no-cache`.
- `net481` on one `windows-latest` job, in `tests/Compatibility/Net481`, which `eng/run-tests.cs` skips off Windows: the only run of the `netstandard2.0` asset off .NET Core.

After 04b, `release/0.2` keeps `release-check` with `0.2.0` as its baseline. On the stack, the 0.2
characterisation suite becomes the baseline side of the 08a/08b differential (split out in 08a, run against the packed `0.2.0`). The suite itself is migrated by 08b, 09, 11 and 12 where a break is intended.

## 2. The executable spec (04c)

- **Target API.** `tests/Resultful.TargetApi/<Package>/`: every public member of every package in its end
  state, with `throw null` bodies, one project per package with the real assembly name. Its `PublicAPI.Shipped.txt`
  is the target list. Gate on every PR: `PublicAPI.Unshipped.txt` additions ⊆ the target, and every shipped 0.2 member the target drops is on `tests/Resultful.TargetApi/removed-in-0.3.txt`, which is empty by 12. At 25 the real API equals the target.
- **Fixtures.** `tests/Resultful.Spec/Fixtures/<ID>/*.cs`, run by `SpecHarness.cs` (merged from #36's
  `CompilationHarness` and #37's `GeneratorTestHarness`).
  - Compile fixtures carry markers checked both ways: `// expect: CS8509`, `// expect: RFG0001`,
    `// expect: RF0001`, `// expect-clean`, `// expect-fix: RF0001`.
  - Run fixtures have static `[SpecCase]` methods. The harness compiles them against the built DLLs, runs the
    generator and analyzers, loads the result and calls each case.
  - IDs: `CORE-*`, `ZERO-*`, `ERR-*`, `VAL-*`, `OP-*`, `ASYNC-*`, the `SHAPE-*`/`HIER-*`/`GEN-*` table in
    `testing-the-shapes.md`, `RF-*` (one per analyzer rule), `UNION-*`, `UERR-*` (error unions, decision 17), `PKG-*`, `HTTP-*`, `AOT-*`, `MIG-*`,
    `UC01`–`UC10`.
  - `UERR-*` fixtures are `net11.0` only and come from `temp/union-error-probe`: a union error in
    `Result<T, TError>` and in a shape (flat `switch`, CS8509 on a missing arm), `default` of the union rejected
    by `Fail` and flagged by the analyzer, `RFG0007` for a case that isn't an `Error`, the JSON `type` is the leaf's
    name, a nested union kept as one case (`PaymentError p =>` is an exhaustive arm; a flat arm for its leaf is CS8121; dropping the inner arm is CS8509 on the inner switch), implicit widening to `Result<T>`, `Fail` allocating 0 bytes,
    CS0029 for a bare leaf into `Result<T, OrderError>` (documented, not a bug). Each has a `[ClosedHierarchy]` twin
    that must give the same output.
  - Round 4 IDs: `UERR-NESTED-*` (nested union `.Error`/`WithContext`), `UERR-CUSTOM-IUNION` (a hand-written `IUnion` struct with a `long` doesn't crash `Fail`), `UERR-JSON-*` (union round-trip, unknown `type` throws), `VAL-EQ`, `VAL-NONEMPTY`, `ZERO-NULL-ERR`, `ERR-TOSTRING`.
  - Quality IDs: `ZERO-TOSTRING` (a zero value's `ToString`/`Equals`/`GetHashCode` don't throw), `ERR-EQ` (structural
    equality with `Context`), `RF-0009`, `RF-0001-ASYNC`, `API-CASE-ADD` (a new case shows as breaking in the API diff),
    `CULTURE-*` (spec and JSON under `tr-TR`), `ASYNC-THROW`/`ASYNC-CANCEL` (exceptions and cancellation propagate),
    `HTTP-500-NOLEAK`.
  - Decision 3's matrix is in from the start: `Result<string, string>`, a `List<string>` success, a null
    success, the zero value, used from a second assembly.
- **Ledger.** `tests/Resultful.Spec/ledger.tsv`: ID, state, owning PR, SHA-256 of the fixture files. A `pending`
  line that passes fails the build ("flip this line"). `done` lines pass on `net10.0` and `net11.0`. The job
  writes `spec-report.json`.

## 3. The end-to-end oracle (04d, then 20b, 22, 23a)

- **The Shop.**
  - `Shop.Domain`: closed hierarchies and shapes, in a separate assembly on purpose.
  - `Shop.Api`: orders (UC1), sign-up (UC2, UC3), context (UC4), a payments adapter (UC5).
  - `Shop.Worker`: a queue handler over an in-memory channel (UC6).
  - `Shop.Legacy`: UC10's "before" code, and a library with an `internal` `[MustObserve]` (UC9).
- **`tests/Resultful.EndToEnd`**, with no `ProjectReference`.
  - Pack every package with a per-run version and restore into an isolated folder (Known traps).
  - Build with `TreatWarningsAsErrors`, `AnalysisLevel latest-all` and nullable on.
  - `http-cases.json`: one request per error leaf, with the status and ProblemDetails body. The OpenAPI document
    lists the success and each failure status.
  - From 22: `PublishAot=true` on `net11.0`. The native binary answers the same cases. IL2xxx or IL3xxx fails.
- **Broken consumers.** `tests/Resultful.EndToEnd/Broken/*` must not build: a missing arm, a discarded result, a
  `default` result, a type derived outside its base's assembly, a bare string into `Result<string>`, an untyped `Match` handler (RF0008), and a reordered `Match` whose typed handlers no longer compile (CS0411 on a generic `Match` after a leaf rename, verified; the untyped version compiles silently, verified). Each writes
  `<ErrorLog>`. The runner compares the SARIF with the expected IDs, both ways.
- **Reference model.** `tests/Resultful.Properties.Tests/Model.cs`: a small class-based result that is plainly
  correct. CsCheck generates random programs (`Map`, `Bind`, `Then`, `OrElse`, `Combine`, `WithContext`, …) and
  runs each on the model, the core, the async overloads and a shape. All must agree.
- **Baseline differential (08a, 08b).** The same programs on `0.2.0` (packed as `Resultful.Baseline`) and on the
  new storage. Every difference is on a committed allowlist: empty for 08a, only the zero value for 08b.

## 4. Generator and analyzers, per Roslyn build and per compiler (13a, 13b, 16a–17)

**In process.** One test source tree, two projects (`.Roslyn414`, `.Roslyn5`), each with its own generator
build and `Microsoft.CodeAnalysis.CSharp`. The same for analyzers.
- Both builds give byte-identical output for every fixture without `[Union]`.
- Fuzzing: CsCheck builds random hierarchies (depth, nesting, generics, accessibility, name clashes). The result
  is a clean compile or an `RFG` diagnostic, never CS8785. Shuffled syntax trees give the same bytes.
- Seeded faults: the harness rewrites generated source (drop the zero guard, swap two arms, return the base for a
  leaf). Every fault must fail a fixture.
- Analyzers: per rule, positive, negative, a user's own type, an `internal` attribute copy. Code fixes with
  fix-all. The suppressor has its own harness reporting suppressed diagnostics.

**The package.** `eng/verify-package.cs` checks:
- `analyzers/dotnet/roslyn<v>/cs/<name>.dll` per build, no `lib/`, `developmentDependency` set.
- Each DLL's `Microsoft.CodeAnalysis` reference is no newer than its folder's version.
- Each satellite dependency group holds only versions for its own target (no 11.x in `net10.0`).

**Four compiler legs** build from the packages: `Shop.Domain` and `Broken/Generator/*` from 13b, `Broken/Analyzers/*` from 17, `Shop.Api` from 20b:

| Leg | Compiler | Target | Must load | Must hold |
|---|---|---|---|---|
| SDK 9.0.3xx | Roslyn 4.14.0 | `net8.0` | `roslyn4.14` | `Match` works, no `[Union]`. Builds `Shop.Domain`, a console driver and the broken consumers only. |
| SDK 10.0.1xx | unverified | `net10.0` | asserted | No `[Union]`, no CS9057. |
| SDK 10.0.4xx | Roslyn 5.9.0 | `net10.0` | `roslyn5.x` | No `[Union]`: the target has no attribute. |
| SDK 11.0.1xx | Roslyn 5.12.0 | `net11.0`, `net10.0` | `roslyn5.x` | `[Union]` on `net11.0` only. A flat `switch` is exhaustive; removing an arm gives CS8509. The `net11.0`-only Shop folder with union errors builds. |

Each leg has its own `global.json`, treats NU1605, NU1608, CS9057, CS8032, CS8785 and AD0001 as errors, and
asserts which analyzer DLL loaded (`dotnet msbuild -getItem:Analyzer`, or the binlog).

## 5. Gates on every PR

| Gate | From |
|---|---|
| API coverage: every member in `Shipped` and `Unshipped`, every package, is called by a test, fixture or law | 01 |
| Target API: `Unshipped` additions ⊆ target, removals on `removed-in-0.3.txt` | 04c |
| Ledger: contract IDs flipped, nothing else changed, fixture hashes match | 04c |
| Test counts never fall, skips never rise (`eng/test-counts.json`); a migrated assertion counts as kept | 02 |
| New suppressions (`NoWarn`, `#pragma warning disable`, `Skip =`, `continue-on-error`) listed in the PR body | 02 |
| Branch coverage ratchet, core only | 02 |
| Doc snippets: tagged blocks in the README and *Errors in practice* equal regions of the Shop (`eng/check-snippets.cs`) | 04d lenient, 25 strict |
| Allocations (`GC.GetAllocatedBytesForCurrentThread`), struct sizes (`Unsafe.SizeOf`), IL and member budgets | 08a, 13b |

Manual only: Stryker.NET at the four mutation runs (survivors in the PR body, no threshold; seeded faults cover
generated code), and BenchmarkDotNet gated on "Allocated = 0", never on time (13b, 24).

## 6. Who owns each layer of `testing-the-shapes.md`

| Layer | Owner |
|---|---|
| 1 Generator output | 13a (`GEN-*` diagnostics), 13b (budgets) |
| 2 Real core against stub | 04c: the target API replaces the stub; 13b runs against the real core |
| 3 Shapes run as code: examples, differential, allocations | 04c fixtures, flipped in 13b and 15; the reference model from 02 |
| 4 Compile-outcome markers | 04c harness; 13b, 17 |
| 5 Cross-assembly, packed package, AOT | 04d, 13b (four legs), 22 (AOT) |
| 6 Generated-member walk, mutation | 13b (walk, seeded faults), mutation runs |
| 7 Samples | 04d, 23a |

## 7. What the orchestrator runs itself, per PR

1. `gh pr checks <n> --json name,bucket`: every job passes. (Never `--watch`.)
2. `gh run download` for `spec-report.json`. The IDs flipped in the ledger, green in the report and named in the
   contract are the same set.
3. `git diff --name-only <prev>..<head> -- tests/Resultful.Spec/Fixtures tests/Resultful.TargetApi` is empty
   unless the PR has `spec:` commits, and the ledger's hashes match.
4. The counts and suppressions gates, read from the PR body and the diff.
5. Size: over about 800 changed lines outside tests means split.
6. After a restack, `git range-diff`: no PR changed except where a conflict was resolved.

## Unverified

- The Roslyn version in SDK 10.0.1xx, and which analyzer folder each compiler picks. The legs assert it.
- `dotnet msbuild -getItem:Analyzer` returning resolved analyzers (fallback: binlog).
- Package validation against a pre-release baseline.
- Stryker.NET with Microsoft.Testing.Platform and xunit.v3. `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` on
  Roslyn 5.x. BenchmarkDotNet on `net11.0`. Native AOT on `ubuntu-latest` with no extra setup.
