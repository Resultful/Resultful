---
type: decision
title: "Tests are a mix: NUnit examples, xunit.v3 property tests"
description: "Keep the NUnit suite; add rig-style xunit.v3 on Microsoft.Testing.Platform with CsCheck and a coverage ratchet."
status: proposed
tags: [resultful, decision]
decided:
  by: ""
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 6. Tests are a mix: NUnit examples, xunit.v3 property tests

The maintainer asked for a large expansion of the tests, and "maybe a mix" of the existing stack and rig's.

Proposed: the existing NUnit + FluentAssertions example tests stay. New suites follow rig: xunit.v3 on
Microsoft.Testing.Platform, CsCheck property tests (monad laws for `Map`/`Bind`, short-circuiting for
`Then`/`Sequence`, error accumulation for `Plus`/`Fold`/`Reduce`, sync/async equivalence), and coverlet
coverage with a ratchet (floor raised as coverage improves, never lowered). Tests run on `net10.0` and
`net11.0`, and one run loads the `netstandard2.0` asset.
