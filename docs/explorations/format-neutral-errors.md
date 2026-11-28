---
type: exploration
title: "Errors on any wire"
description: "The rules Resultful.Text.Json applies to errors are format-neutral, and three packages already copy them with drift; what the core and the generator would need so YamlDotNet, MessagePack or Newtonsoft can share them."
status: draft
tags: [resultful, exploration, serialization, generators]
generated:
  by: claude-opus-5.5
  at: 2026-11-28
---

# Errors on any wire

> Draft exploration behind [decision 25](../decisions/0025-format-neutral-error-wire.md), which is **proposed**.

## What is format-neutral today, and where it lives

`Resultful.Text.Json` (about 1,100 lines) is all System.Text.Json, but most of what it decides about an error isn't:

| Rule | Text.Json | Elsewhere |
|---|---|---|
| A leaf's name on the wire | `ErrorWire.WireName`: `ValidationFailed<CustomerError>`, arguments joined with `,` | Diagnostics (#78) `ErrorDescription.TypeName`: joined with `, `. AspNetCore `ResultfulHttpOptions.ValidationKey`: `GetType().Name`, so ``ValidationFailed`1`` |
| The keys `type`, `message`, `context`, `cause` | `ErrorWire` constants | HotChocolate (#80) has its own GraphQL field names |
| "Is this a `ValidationFailed<T>`" | `ErrorWire.IsValidationFailed` | `ErrorProblems.IsValidationFailed` (AspNetCore), `ResultfulValidationError.IsValidationFailed` (#80) |
| Which leaves an error type can hold | `ErrorCaseRegistry`, per `JsonSerializerOptions`, from `AddErrorCases<TError>(cases)` | #80 `ResultfulGraphQLOptions.AddErrorCases<TError>(cases)`, its own copy and checks |
| Wrapping a leaf back into an error union | `ErrorUnionJsonConverter<TUnion>(cases, wrap)` | nowhere else yet |
| The zero value | write refuses it (decision 10) | each package on its own |

The three spellings of one type name are the drift that matters: a client that keys on `type` from JSON, a log
query on the Diagnostics tag and a ProblemDetails `errors` key see three different strings for the same error.

## Reading a leaf without System.Text.Json

Text.Json reads a leaf through STJ's own contract (constructor binding, source-generated `JsonTypeInfo` for AOT).
Other serializers lack one or both:

- **YamlDotNet 16.3.0** (MIT), probed by hand: writing a positional record leaf works; reading fails ("Failed to
  create an instance of type 'Suspended'"). It has no constructor binding, and every Resultful leaf is a positional
  record. Its static generator covers AOT for types it can construct.
- MessagePack and Newtonsoft bind constructors, but by reflection (Newtonsoft) or with their own attributes.

The generator already knows each leaf of a `[ClosedHierarchy]` or error union: its name, its primary constructor and
the type of each parameter. Emitting that as a plain descriptor (name, members with types and getters, a factory from
member values) gives every format the same AOT-safe way in, with no reflection.

## Testing it once

The Shop's JSON round-trip law (any `CustomerResult` survives write then read) is the start of a conformance kit:
nested unions, `ValidationFailed`, a cause chain, context, an unknown `type`, and the zero value refused. Written once
over an abstract "write, then read" pair, each format package runs it.

## Not settled

- Whether the registry belongs in the core (dependency-free, decision 1) as plain types, or in a small shared
  package that Text.Json, AspNetCore, HotChocolate and Diagnostics reference.
- Whether Text.Json switches its read path to the descriptors (one path) or keeps STJ binding (honours
  `[JsonPropertyName]` and STJ converters on members).
- The name rule's exact form. `ValidationFailed<CustomerError>` (no space) is what Text.Json already writes, so it
  costs no wire change; Diagnostics and AspNetCore would change.
