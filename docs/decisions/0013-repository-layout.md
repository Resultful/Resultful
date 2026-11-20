---
type: decision
title: "Repository layout: src, tests, samples, benchmarks"
description: "Projects move under src/, tests/, samples/ and benchmarks/ with a .slnx solution, following the sibling projects."
status: stable
tags: [resultful, decision, structure]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 13. Repository layout: `src/`, `tests/`, `samples/`, `benchmarks/`

Projects move to the layout in [the architecture](../architecture.md):
- `src/` for packages
- `tests/` for test projects, including a `netstandard2.0` asset run and an AOT smoke test
- `samples/` for runnable integrations
- `benchmarks/` for BenchmarkDotNet

The solution becomes `Resultful.slnx`. Package settings move to `src/Directory.Build.props`.

Why:
- A flat root with one project works only while there is one package. The satellite family
  ([decision 11](0011-package-family.md)) needs a place for each kind of project.
- The sibling projects (Iceberg.NET, Diffra, and `Resultful.Assertions`) already use `src/` and `tests/`.

How: one pull request at the start of M1, with no code changes. It updates:
- the `.ls-lint.yml` globs
- the paths in `ci.yml`
- the test/example conditions and the pack paths in `Directory.Build.props`
- the `PublicAPI` `AdditionalFiles`
- the path the package check is run with

It goes first so that later diffs aren't buried in moves.
