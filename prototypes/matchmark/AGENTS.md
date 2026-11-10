# AGENTS.md: Matchmark

Start here to continue work on the Matchmark prototype. This page has the working rules, every check and how to run it,
the backlog in order, and what's waiting or blocked. The why is in the pages it links to. Don't repeat it here; link to it.

## Read first

| Page | For |
|:--|:--|
| [VISION.md](VISION.md) | Where it's going: the roadmap (steps 1 to 6) and the principles every change follows |
| [docs/decisions.md](docs/decisions.md) | What's decided (Q-numbers), and why |
| [docs/QUESTIONS.md](docs/QUESTIONS.md) | What's open, and the assumption work proceeds on meanwhile |
| [docs/reference/model.md](docs/reference/model.md) | The model and its shape in the pipeline: the next big refactor |
| [docs/reference/testing.md](docs/reference/testing.md) | The test layers, and what's next to take |
| [README.md](README.md) | The projects, and the mechanics |
| [GAPS.md](GAPS.md) | Where the prototype falls short, each with its status |

## Working rules

- **Questions don't block.** A decision that's the maintainer's goes in `docs/QUESTIONS.md`: the question, what decides
  it, and the assumption work proceeds on. Then carry on under the assumption. When it's answered, the answer moves to
  `docs/decisions.md` with its date and number, and the question is deleted.
- **Reference pages say built or planned.** Every item in `docs/reference/` is marked one or the other, so a page
  describes the code as it is. When something is built, update its page, its `Status` row, and the roadmap tick in
  `VISION.md` in the same commit.
- **Every check is checked.** A gate counts only if breaking what it guards makes it fail. Every test asserts what it
  examined ("examined N", `NotBeEmpty`), so an empty run can't pass. A new check is shown failing on an injected fault
  (see `SpecRegressionTests`).
- **Generated output is never committed.** It's derived and diffed (`DerivedEvidenceTests`, CI's `derived` job). There
  are no golden files: a model or surface change shows in the diff.
- **Decided once.** A naming, typing or support decision lives in the model (`Emitter.Operations`, `ApiModel`), and no
  output re-derives it. An output that needs something new adds it to the model and to `ApiModelTests`.
- **No behaviour change on a move.** A refactor keeps every test green, one output at a time.
- **Diagnostics are documented.** A new `MM` descriptor gets a row in `docs/reference/diagnostics.md`. `DiagnosticDocsTests`
  fails otherwise.
- **Readers are Matchmark's own** (Q56). AsyncAPI, Arazzo and Overlay are read by `Matchmark.Specs.*` (netstandard2.0,
  shipped in the generator's analyzer folder). A change to a reader keeps `Matchmark.Specs.Tests` green: every vendored
  fixture and example, and agreement with the official schema on every mutation. What a reader leaves to others is
  named in its summary and excluded from the comparison explicitly, never silently.
- **Licences.** Dependencies are permissive (MIT, Apache-2.0, BSD). AwesomeAssertions was chosen over FluentAssertions,
  and Corvus.Json.Validator over JsonSchema.Net, for this reason. Add every package to `Directory.Packages.props` with
  a comment saying why.
- **Git.** Commit subjects are conventional (`feat: …`, `test: …`, `docs: …`, with an optional scope such as `feat(generator): …`). Work goes
  on the session's branch, rebased and never merged. Open a pull request only when the maintainer asks. History is linear: pull
  requests are squash-merged, branches are rebased (never merged) and deleted on merge. Scratch files, local databases
  and worktrees go in a gitignored `temp/`, never in the tree.

## Checks

From the repository root:

| Check | Command | Notes |
|:--|:--|:--|
| Everything | `dotnet test Matchmark.slnx -c Release` | Specs 830 (65 skipped), Testing 161, Generator 280 (2 skipped), Sample 121 |
| The readers | `dotnet test Matchmark.Specs.Tests -c Release` | Matchmark's own AsyncAPI, Arazzo and Overlay readers against everything vendored in `specs/`: fixtures, official schemas, compliant sets, examples, and mutations compared with the official schemas |
| Large specs | `sh samples/large/fetch.sh`, then `MATCHMARK_LARGE_SPECS=$PWD/samples/large dotnet test Matchmark.Generator.Tests -c Release --filter LargeSpecTests` | GitHub's and Stripe's specs, fetched and not committed. They must compile with 0 errors; the share generated (98.7%, 99.8%) should only rise |
| The specifications' examples | part of `dotnet test` (`OfficialExamplesTests`) | Vendored in `specs/` at pinned commits; 57 of 57 |
| Benchmarks | `dotnet run -c Release --project Matchmark.Benchmarks -- --filter '*'` (add `--job short` for a quick look) | BenchmarkDotNet over reading, normalising, the parse step, a whole generator run, the suite, samples and violations; GitHub and Stripe join when fetched. A performance claim cites these, run before and after ([testing.md](docs/reference/testing.md#benchmarks)) |
| On a broker | `docker run -d --network host nats:2.10`, then `MATCHMARK_NATS_URL=nats://127.0.0.1:4222 dotnet test Matchmark.Generator.Tests -c Release --filter MessagingTests` | The generated AsyncAPI producer and consumer on NATS; CI runs a NATS service container |
| More random specs | `MATCHMARK_RANDOM_SPECS=200 dotnet test Matchmark.Generator.Tests -c Release --filter RandomSpecTests` | 12 seeds by default. A failure prints a shrunk spec: commit it as a fixture |
| Derived evidence | `MATCHMARK_DERIVED=<dir> dotnet test Matchmark.Generator.Tests -c Release --filter DerivedEvidenceTests` | Writes `{sample}.model.json`, `.api.txt`, `.g.cs`. Diff two runs to review a change |
| The package | `eng/consume-package.sh <work dir>` | Packs the generator and builds a fresh project from it, with warnings as errors, applying an overlay through the readers in the package |
| Refresh the specifications | `sh specs/vendor.sh` | Re-vendors `specs/` at the pins in the script and rewrites `specs/SOURCES.md`. Moving a pin is a change of its own, with the reader suite run before and after |
| The image | `docker build --tag matchmark .` | Then the smoke tests in the `image` job of `.github/workflows/ci.yml` |

The ledgers (`corpus-ledger.txt`, `large-ledger.txt`, `examples-ledger.txt`) are written to
`Matchmark.Generator.Tests/bin/Release/net10.0/`.

## Backlog, in order

Each item links to the page that defines it. Update that page as items are built.

1. **Emitters onto the shape** (roadmap step 2). The parse step (`ApiShape`), Verify on the shape, and the per-tag
   partition are built. Next is moving the emitters to be pure functions of a shape: endpoints first, then handlers and
   client, then components. Each step adds an `IncrementalityTests` case.
   [model.md: Its shape in the pipeline](docs/reference/model.md#its-shape-in-the-pipeline).
2. **The engine on the model**: once question 1 is decided. [model.md: Status](docs/reference/model.md#status).
3. **Verify** (roadmap step 3): every version read through the standard libraries. The libraries are measured against
   the corpus (`ReaderLibraryTests`). Where they can run is question 5: none targets netstandard2.0 for the generator,
   BinkyLabs Arazzo has no 1.1, and BinkyLabs Overlays fails the licence rule.
4. **Flip** (roadmap step 5): a trial end to end on a real service with a frontend. The typed AsyncAPI consumer and
   producer, with NATS as the first broker, are built ([build.md: Messaging](docs/reference/build.md#messaging)).
5. **Later layers** (roadmap step 6), as `VISION.md` lists them.

Smaller open items are each page's "Planned" rows: `validation.md`, `invariants.md`, `sessions.md`, `twins.md`.

## Waiting on the maintainer

- **The split.** This folder is laid out as its own repository (see [Its own repository](#its-own-repository)). Its
  CI, `.github/workflows/ci.yml`, runs only once the folder is a repository's root, so none of its jobs has run on
  GitHub yet: test, large specs and examples, image, package, and derived evidence. Each has been run locally, and the
  whole suite passes in a copy of this folder on its own.
- **The Resultful issue**: `default(Result<,>)` reads as a success. Drafted in
  [docs/resultful-default-issue.md](docs/resultful-default-issue.md), to be filed on Resultful/Resultful.
- **Questions 1 to 4** in [docs/QUESTIONS.md](docs/QUESTIONS.md). Each has an assumption in force, so nothing waits.

## Blocked

These need access or decisions this repository can't give:

- **Its own home** (roadmap step 1): creating `rtkelly-labs/matchmark` and moving this folder there with its history. A
  session can't create repositories.
- **The Oxbow forks** of the standard readers (roadmap step 3).
- **Publishing**: packages, the dotnet tool, and the image to a registry.
- **The trial**: a real service with a frontend for the flip trial, a broker for AsyncAPI channels, and a sandbox with
  credentials for scheduled fidelity runs.

## Its own repository

This folder is laid out to be a repository's root. Nothing in it refers to files outside it:

- **Build settings.** `Directory.Build.props` and `Directory.Packages.props` hold every setting and version: warnings
  as errors, the analysis level, and central package versions. They import nothing from above.
- **SDK, style and licence.** `global.json` pins the SDK (10.0.100, rolling forward to the latest feature band),
  `.editorconfig` sets the style, and `LICENSE` is MIT.
- **Resultful.** It comes from NuGet (`Resultful` 0.2.0-beta05), for the samples and the opt-in adapter's tests. The
  generator and the runtimes don't reference it.
- **CI.** `.github/workflows/ci.yml` runs from the root, with no paths filter.
- **The image.** The `Dockerfile` builds with the root as its context (`docker build -t matchmark .`).

To split it out with its history (the maintainer's step):

```bash
git clone <this repository> matchmark && cd matchmark
git filter-repo --subdirectory-filter prototypes/matchmark   # or: git subtree split --prefix=prototypes/matchmark
git remote add origin https://github.com/rtkelly-labs/matchmark.git && git push -u origin main
```

Then, in the new repository:

- **Branch protection on `main`:** squash merges, linear history, and the CI jobs as required checks.
- **Package metadata:** `PackageProjectUrl` and `RepositoryUrl` in `Directory.Build.props` already point at
  `rtkelly-labs/matchmark`.
- **The "Waiting" and "Blocked" sections above:** remove what the split resolves.

To check the folder stands alone before splitting: copy it somewhere outside this repository, delete `bin` and `obj`,
and run `dotnet test Matchmark.slnx -c Release`.
