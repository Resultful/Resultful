---
type: decision
title: "Assertions live in Resultful.Assertions"
description: "The FluentAssertions.Resultful repository becomes Resultful.Assertions: assertion packages for result and union types across assertion libraries, keeping the existing package IDs."
status: stable
tags: [resultful, decision, assertions]
decided:
  by: human:rtkelly13
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 12. Assertions live in `Resultful.Assertions`

The maintainer chose the name. The `Resultful/FluentAssertions.Resultful` repository is renamed
`Resultful/Resultful.Assertions`. It is the home for many assertion projects for result, option and union types
(OneOf, Resultful and others), in every assertion library where the library's extension model makes that
worthwhile. Assertions for Resultful are one part of it, not the whole point.

- **Existing packages keep their IDs.** `FluentAssertions.OneOf` (about 388,000 downloads, the family's most
  used package, and mostly used without Resultful), `FluentAssertions.Union` and
  `FluentAssertions.OneOf.Extended` stay as they are. None of them depends on `Resultful`.
- **Proposed shape for the new packages: an independent core plus adapters.**
  - `Resultful.Assertions` has `ShouldSucceed()`, `ShouldFail()` and `ShouldFailWith(...)`, and throws an
    exception that every test framework reports as a failure. It depends only on `Resultful`.
  - `Resultful.Assertions.<Library>` adapters (FluentAssertions, AwesomeAssertions, then Shouldly and TUnit)
    add each library's native style.
  - One package referencing every assertion library would force all of them on every consumer.
- **FluentAssertions licensing.** FluentAssertions 8 and later is commercial, while 7.x and AwesomeAssertions are
  not. The adapter references the lowest version it supports, and a consumer who resolves 8 or later carries that
  licence. Which range the adapter supports is still to be checked.
- `FluentAssertions.Resultful` is deprecated on NuGet in favour of `Resultful.Assertions.FluentAssertions` once
  that exists.
- **It publishes only what changed.** That repository holds many independent packages, so it versions and
  releases each package on its own. This repository does the same ([decision 11](0011-package-family.md)).
  Publishing stays a manual workflow run.
- That repository gets its own vision and OKF docs bundle. Its vision is about testing failure-as-a-value
  types, of which Resultful is one.

Why:
- The dependency direction: assertion packages depend on Resultful, OneOf and an assertion library, and nothing
  in this repository depends on them.
- Keeping `FluentAssertions.OneOf` where it is avoids disturbing its users. Renaming the repository costs
  nothing, because GitHub redirects the old URLs.

Related: this repository's own tests don't use `Resultful.Assertions`, because it is built against a published
`Resultful` ([decision 6](0006-tests-are-a-mix.md)).
