---
type: decision
title: "M5 ecosystem candidates and samples"
description: "Candidate satellites (Http, MVC, Verify, gRPC, CsCheck generators) and samples (Polly v8, EF Core, OpenTelemetry, MIT mediator) to confirm at M5. Extends decision 11."
status: proposed
tags: [resultful, decision, packages, roadmap]
decided:
  by: human:rtkelly13
  at: 2026-11-21
generated:
  by: claude-opus-5.5
  at: 2026-11-21
---

# 21. M5 ecosystem candidates and samples

This extends [decision 11](0011-package-family.md). Each candidate below is confirmed individually when M5 reaches it.

## Candidate packages

- `Resultful.Http`: the client side of `Resultful.AspNetCore`. Reads ProblemDetails back into typed errors
  (`GetFromJsonAsResultAsync<T>()`), using in-box `System.Net.Http.Json`. Works with Refit and typed clients.
- MVC controllers in `Resultful.AspNetCore`: `ToActionResult()` and `ActionResult<T>`.
- Verify converters for snapshot tests (in the assertions family, [decision 18](0018-resultful-assertions-live-here.md)).
- `Resultful.Grpc`: errors to `Status` and rich error details.
- CsCheck generators (`Gen<Result<T>>`) for consumers' property tests.

## Samples, not packages

- Polly v8 (`HandleResult` on failure), EF Core, OpenTelemetry `error.type`.
- The mediator sample uses an MIT-licensed mediator: MediatR 13 and later is dual-licensed (RPL-1.5 or commercial),
  for the same reason the FluentAssertions adapter stays on 7.x.
