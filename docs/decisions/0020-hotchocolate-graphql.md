---
type: decision
title: "Resultful.HotChocolate maps error cases to GraphQL error types"
description: "Resultful.HotChocolate lets resolvers return Result or generated shapes directly and projects error leaves as GraphQL union/payload error types. Extends decision 11."
status: stable
tags: [resultful, decision, packages, graphql, hotchocolate]
decided:
  by: human:rtkelly13
  at: 2026-11-21
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# 20. `Resultful.HotChocolate` maps error cases to GraphQL error types

This extends [decision 11](0011-package-family.md). Its rules apply unchanged: the core references nothing, and a
satellite targets what its dependency targets.

A result's error cases become GraphQL types that clients can query, with no exceptions thrown.

- HotChocolate's query and mutation conventions already model this. A resolver returning
  `FieldResult<User, UserNotFound>` gets the field type `union UserByIdResult = User | UserNotFound`, and every
  error type implements `interface Error { message: String! }`. Mutations get a payload with
  `errors: [UpdateUserError!]`.
- This package lets a resolver return `Result<T, TError>` or a generated shape directly. A type-discovery handler
  and a middleware read the generator's case list, with no reflection, and register each leaf as an error type.
  `FieldResult` stops at five error types; this has no limit.
- `Error.Message` maps to `message`. `Context` and the cause chain aren't exposed unless the app opts in (the same
  rule as `Error.ToString()` and the HTTP 500 body).
- A GraphQL union can't contain another union, so a nested error union is flattened to its leaves in the schema.
  The C# side keeps the nesting.
- `ValidationFailed` becomes a `ValidationError` type with the failing fields. An error that isn't a declared case
  goes in the top-level `errors` array with `extensions.code` set to the leaf's name, and with no message outside
  Development (the same as HTTP 500).
- A `ToFieldResult()` conversion comes first, for code that already returns `FieldResult`.
- Targets what HotChocolate 16 targets.

Why:
- GraphQL's "errors as data" pattern is a typed union of outcomes, which is what a Resultful shape already is.
  HotChocolate's `FieldResult` shows the model fits; this package removes the translation step.
