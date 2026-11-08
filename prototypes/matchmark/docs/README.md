# Matchmark documentation

Two kinds of document live here. **Explanations** say why and how, and are read once. **Reference** pages say exactly
what exists, and are looked up. Each reference page marks every item as built or planned, so it can be trusted as a
description of the code today and of the design ahead.

## Explanations

| Document | Read it for |
|:--|:--|
| [VISION.md](../VISION.md) | Where Matchmark is going and why: verify, hosted sessions, flip, in that order. |
| [CONCEPT.md](../CONCEPT.md) | How the prototype works today, end to end. |
| [PETSTORE.md](../PETSTORE.md) | The sample, walked through. |
| [LAYERS.md](../LAYERS.md) | How behaviour beyond the OpenAPI document is taken on, and what the corpus found. |
| [COMPLETENESS.md](../COMPLETENESS.md) | What makes a spec complete enough to test, and the invariant grammars. |
| [GAPS.md](../GAPS.md) | Where the prototype falls short of the vision. |
| [samples/README.md](../samples/README.md) | The corpus and its ledger. |

## Reference

| Page | What it covers |
|:--|:--|
| [Command line](reference/cli.md) | Every `matchmark` command, its options and exit codes. |
| [Build settings](reference/build.md) | MSBuild items and metadata, generated parts, and the runtime packages that switch features on. |
| [Specifications and versions](reference/specifications.md) | Which formats and versions are read, the version policy, and the libraries behind each. |
| [Support matrix](reference/support-matrix.md) | Format and version against read, verify, host, generate and write, generated from the tests. |
| [Checks](reference/checks.md) | Every check the regression engine runs, and what each proves. |
| [Diagnostics](reference/diagnostics.md) | Every `MM` diagnostic the generator reports. |
| [Extensions](reference/extensions.md) | The `x-matchmark-*` and `x-dependencies` extensions, their grammars and where they apply. |
| [Invariants across a collection](reference/invariants.md) | Planned: unique, references, derived, conserved, scoped, limit and order, each declared once and used by the twin, the suite, the audit and the generator. |
| [Validation](reference/validation.md) | Planned: per-object checks on .NET's own validation layer, with a generated resolver. |
| [Hosted sessions](reference/sessions.md) | A copy of the API per test session: routing, ids, tokens, pathways and the report. |
| [Twins and fidelity](reference/twins.md) | The stateful twin, fidelity runs, recordings and the gated loop. |
| [The model](reference/model.md) | The semantic model every output is generated from (roadmap step 2). |
| [Flip](reference/flip.md) | Moving an existing application to spec-first: derive, verify, scaffold, verify at compile time. |
| [Glossary](glossary.md) | The terms these pages use. |

## Decisions and questions

- [Decisions](decisions.md) records each decision about the product, why it was made, and when.
- [Questions](QUESTIONS.md) holds what is still open, each with the assumption work proceeds on until it's answered.

Status markers used throughout: **Built** (in the code and tested), **Partial** (some of it built; the rest named),
**Planned** (decided, not built).
