---
okf_version: 0.2
type: guide
title: "Using Resultful with AI Agents"
description: "Drop-in AGENTS.md / CLAUDE.md conventions so coding agents write idiomatic Resultful in consuming applications."
status: stable
tags: [resultful, guide, agents, llms]
generated:
  by: agent:antigravity
  at: 2026-11-21
---

# Using Resultful with AI Agents

Copy the block below into your application's `AGENTS.md` or `CLAUDE.md` so AI coding agents generate idiomatic, analyzer-clean Resultful code on the first pass:

```markdown
## Error Handling (Resultful)
- Model expected domain failures as sealed `record` leaves under a `[ClosedHierarchy] abstract partial record` inheriting `Resultful.Error` (or a `.NET 11` error union).
- Use `Validation<T, TError>` (`Combine`) at input boundaries to collect all validation errors; use `Result<T, TError>` (`Map`/`Bind`) in domain workflows to fail fast.
- Use `[ResultShape]` on a `readonly partial struct` for public domain operations with multiple error cases.
- Never return `default` of any result/validation/option type; always return `Result.Ok(...)` / `Result.Fail(...)` or an implicit leaf conversion.
- Never discard a `Result` or `Task<Result>` silently (satisfy `RF0001`); observe via `Match`, `Switch`, `TryGetValue`, or boundary mapping (`ToHttpResult()`).
- Never put PII or internal diagnostics into `Error.Message`; attach internal context via `.WithContext(...)` (excluded from `ProblemDetails`, GraphQL `message`, and OTel spans by default).
- Never catch `OperationCanceledException` inside result pipelines; use `Result.Try(...)` only at I/O boundaries where exceptions are expected.
- In tests, prefer `Resultful.Assertions` (`ShouldSucceed()`, `ShouldFailWith<TError>()`) over manual `IsSuccess`/`Value` checks.
```

## Machine-readable references

- **Portal `llms.txt`:** `docs.ryankelly.dev/resultful/llms.txt` (evergreen guides and API reference, excluding historical release notes).
- **Exact public API surface:** `src/*/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` in this repository.
- **Design rationale:** [Architecture Decisions](../decisions/index.md).
