---
type: decision
title: "The package shape is checked in CI"
description: "Dependency groups, floors, metadata and symbols are inspected on every pack."
status: stable
tags: [resultful, decision]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 5. The package shape is checked in CI

Every pack is inspected and the build fails on: a `NETStandard.Library` or `System.ValueTuple` dependency; a
`.NETStandard1.x` or `.NETFramework` dependency group; a `lib/netstandard1.x` asset; any dependency at all in
core (from [decision 1](0001-core-is-dependency-free.md)); a OneOf floor below 3.0.271 in `Resultful.OneOf`; a
missing licence expression, readme, repository URL or commit; a missing `snupkg`.

Pre-release versions keep the `betaNN` form, because NuGet compares pre-release labels lexically and
`beta.6` would sort below `beta05`.

Why: beta05 shipped a bogus `.NETFramework5.0` group and a `netstandard1.3` chain, and nothing noticed until a
consumer's restore failed.
