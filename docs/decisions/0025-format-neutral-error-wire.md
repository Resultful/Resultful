---
type: decision
title: "One error wire, shared by every format"
description: "The core owns the wire name, keys and case registry for errors; the generator emits an AOT-safe descriptor per leaf; one conformance kit tests every format; a thin Resultful.YamlDotNet is the first new format."
status: proposed
tags: [resultful, decision, serialization, generators]
decided:
  by:
  at:
generated:
  by: claude-opus-5.5
  at: 2026-11-28
---

# 25. One error wire, shared by every format

> The drift found, the YamlDotNet probe and the open questions are in
> [Errors on any wire](../explorations/format-neutral-errors.md).

`Resultful.Text.Json`'s rules for errors are format-neutral, and AspNetCore, HotChocolate (#80) and Diagnostics
(#78) already keep their own copies, which disagree on any generic error leaf: `NotFound<Guid>` is written
`NotFound<Guid>`, `NotFound<Guid>` and ``NotFound`1``, and `Conflict<int, string>` three ways. This moves the rules to
one place, in order. Each step is useful alone.

## Proposed

1. **The core owns the rules** (no new dependency, decision 1):
   - one wire-name function: `ValidationFailed<CustomerError>`, what Text.Json writes today;
   - the wire keys `type`, `message`, `context`, `cause`, and one `IsValidationFailed`;
   - one `ErrorCases` registry: the leaves each error type can hold (from the generated `Cases`), plus each union's
     `Wrap`. Text.Json, AspNetCore, HotChocolate and Diagnostics use it instead of their copies.
   Text.Json's wire doesn't change; Diagnostics' type tag and AspNetCore's default validation key do, which is a
   behaviour change to note in their release notes.
2. **The generator emits a leaf descriptor** per `[ClosedHierarchy]` and error-union leaf: wire name, members with
   their types and getters, and a factory from member values. Plain code, no reflection, so AOT-safe; it is what a
   format without constructor binding (YamlDotNet) needs to read a positional record.
3. **One conformance kit**: the Shop's JSON round-trip law, generalised over a write/read pair; nested unions,
   `ValidationFailed`, causes, context, an unknown `type`, the zero value refused. Each format package runs it.
   Where it lives (`Resultful.Assertions`, #81, or a test-only helper) is open.
4. **`Resultful.YamlDotNet`**, thin: an `IYamlTypeConverter` over items 1 and 2, AOT through YamlDotNet's static
   generator. Its licence (MIT) is for the maintainer to accept.

## Open (for the maintainer)

- The registry in the core, or a small shared package (the core would then gain only the name and keys).
- Whether Text.Json reads through the descriptors too (one path) or keeps STJ binding (honours `[JsonPropertyName]`).
- Order: items 1-3 then 4, or a YamlDotNet prototype first to prove the descriptor's shape.

## Delivery

| PR | Contents |
|---|---|
| this | the exploration and this proposal; docs only |
| next | item 1 for Text.Json and AspNetCore, the two on this stack; Diagnostics (#78) and HotChocolate (#80) move when they are restacked onto it |
| then | item 2 with item 3, then item 4 |

Why:
- One error should have one name everywhere a client, a log query or a test can see it.
- Each new format today means copying the registry and the name rule again, and YamlDotNet can't read a leaf at all
  without a factory the generator already has the facts for.
- Keeping the rules in plain core types keeps the core dependency-free and every format package thin.
