# Testing the generator

How Matchmark proves what it generates, layer by layer. Cross-checked against Parquet.SourceGenerator, which tests a
generator the same way at the layers that apply. Each layer catches a failure the others can't, so together they stand
in for a golden-file suite, which Matchmark deliberately doesn't have: generated output is **derived and diffed, never
committed** (Diffra's approach to generated assets).

| Layer | Proves | Built as |
|:--|:--|:--|
| 1. The model | The decisions every output is a function of: names, types, outcomes, support | `ApiModelTests`, from small specs, independent of any output |
| 2. Incrementality | An edit re-runs only what it touches | `IncrementalityTests`, from Roslyn's step tracking on the named steps (`TrackingNames`) |
| 3. Diagnostics | Each one fires where it should, and the reference matches the descriptors | the diagnostic tests; `DiagnosticDocsTests` |
| 4. Generated code is code | It parses, compiles and loads, for every sample and the large specs | `GeneratedServer`, the corpus, `LargeSpecTests` |
| 5. Behaviour | The generated server, client and fake do what the spec says | the spec's own regression suite run against each generated server; the round-trip check of ASP.NET Core's served document |
| 6. Derived evidence | What changed, for review, against the pull request's base | `DerivedEvidenceTests` and CI's `derived` job |
| 7. Breadth | Every construct in every supported version | the official examples (57 of 57), GitHub's and Stripe's specs, the support matrix |
| 8. Consumption | It works as a package and an image | the container image job; a packed-package test is next |

## 1. The model

`ApiModel.Read(spec)` gives what the emitter decides: each operation's parameters (typed, with where they bind from), body
and outcomes, and the diagnostics. Tests pin these from small specs. A test that asserts a substring of generated text
pins an implementation detail instead; new ones go here, or to the derived evidence.

## 2. Incrementality

The pipeline's steps are named (`TrackingNames`: specs, references, referenced resolvers, tag slices). The tests reuse
one driver across edits and assert what each step did:

- an edit to C# that isn't a spec: every step and every output reused;
- a spec with a comment or blank lines added: the text changed, the slices didn't, so no tag re-emits;
- an edit to one tag's operation: exactly one slice modified, one reused, and only that tag's file re-emitted;
- the same input twice: the same bytes (generated code is commit-deterministic).

## 3. Diagnostics

Every descriptor is in [diagnostics](diagnostics.md) with its severity and title, and every id there is one the generator
has or is marked retired (`DiagnosticDocsTests`). Its first run found MM029's title out of step.

## 6. Derived evidence

Per sample (every `samples/*/openapi.yaml` with its overlays, the split sample from its bundle, and the Pet Store),
`DerivedEvidenceTests` writes, to `MATCHMARK_DERIVED`:

| File | What it is | A change means |
|:--|:--|:--|
| `{sample}.model.json` | the model: operations, parameters, bodies, outcomes, diagnostics | a decision changed: review it |
| `{sample}.api.txt` | the generated public surface, signatures only, in a stable order | the API consumers compile against changed: review it |
| `{sample}.g.cs` | every generated source | a body changed: information, unless the surface or the model did too |

On a pull request, CI derives the head and the merge-base (in a worktree) and puts the counts and the model and surface
diff in the run's summary. A base that can't be derived is reported as unavailable, never as a pass. Nothing is
committed, so no golden file churns, and nothing compares the pull request against itself.

**Next** (Diffra's model): a required check bound to the digest of the reviewed change, so an approval goes stale when
the output changes again; the derived files as Diffra evidence once its CLI is released.

## Learned from Parquet.SourceGenerator

Taken: diagnostics as equatable data with positions; step-level incrementality tests on reused drivers; an "examined N"
assertion in every gate so an empty run can't pass; signature-only surface renderings, with a signature change needing
review and a body change not; derived outputs diffed against the base rather than committed.

Next to take: an emitted-code analyzer gate (generated code compiled under every analyzer, with a baseline that must stay
empty); an **independent oracle** (Matchmark's generator, engine and twin all read specs through the same `SpecJson`, so
a second, separately written schema validator would catch a misreading they share); random specs, generated, compiled
and regressed against the twin, minimised fixtures committed; a packed-package consumption test.

Avoided: its 4.0.1 Roslyn floor (Matchmark is on 4.14, so steps carry tracking names); a parser and an emitter of 2,400
lines each (Matchmark's emitter is split by output, the largest file under 900 lines).
