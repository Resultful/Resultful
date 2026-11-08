# Diagnostics

Every diagnostic the generator reports, in the build output and the editor. Nothing it can't generate is skipped
silently (principle 5): the construct and the reason are named.

## Reading the spec

| Id | Severity | Title | When |
|:--|:--|:--|:--|
| MM001 | Error | OpenAPI document could not be read | The file isn't a document the reader can build anything from. Nothing is generated for it. |
| MM002 | Warning | OpenAPI construct not supported | A construct the generator can't write yet; it names the construct and skips it, and only it. |
| MM003 | Warning | Operation has no operationId | The operation is skipped: generated names come from operationIds. |
| MM007 | Warning | The OpenAPI reader objected to part of the spec | Microsoft.OpenApi reported a problem but built a document; code is generated from the rest. |
| MM008 | Warning | Arazzo document could not be read | A `MatchmarkArazzo` companion couldn't be read; its workflows aren't in the suite. |
| MM009 | Warning | Typed as JSON | A `oneOf` or `anyOf` with no discriminator, or a schema that is any JSON value, is typed `JsonElement`: it compiles and round-trips, but nothing checks its shape. Add a discriminator to type it. |

## What the spec leaves out

| Id | Severity | Title | When |
|:--|:--|:--|:--|
| MM004 | Warning | Request validation response is not documented | An operation with a request body documents no `400` with a validation problem body. Validation failures are returned anyway. |
| MM005 | Warning | No example for a constrained string | A `pattern` the generated sample doesn't match; give it an example so the fake can answer with a valid body. |
| MM024 | Info | The spec leaves out something the suite would use | With `MatchmarkAudit="true"`: one per `matchmark audit` finding, naming what saying it would unlock. |
| MM025 | Info | Invariants declared but not enforced | The spec declares rules or dependencies, but `Matchmark.Rules` isn't referenced, so the server doesn't enforce them. |

## Project setup

| Id | Severity | Title | When |
|:--|:--|:--|:--|
| MM006 | Warning | The Regression part needs Matchmark.Testing | `MatchmarkParts` includes `Regression`, but the project doesn't reference the engine. |
| MM026 | Error | Owned problem types are for a contract without a server | `MatchmarkProblems="Owned"` together with a `Server` or `FakeServer` part, which write ASP.NET Core's own problem results. Generate them in a project without it. |

## Verifying hand-written endpoints

With the `Verify` part, hand-written controllers and Map calls are compared with the spec, read the way ASP.NET Core's
own OpenAPI document reads them. Each is reported at the line that's wrong.

| Id | Severity | Title | When |
|:--|:--|:--|:--|
| MM010 | Warning | Operation not implemented | A spec operation has no endpoint. |
| MM011 | Warning | Endpoint not in the spec | An endpoint isn't documented. Document it, or leave it out of the description (`[ApiExplorerSettings(IgnoreApi = true)]`, `ExcludeFromDescription()`). |
| MM012 | Warning | Documented response not declared | The spec documents a response the endpoint doesn't declare. |
| MM013 | Warning | Declared response not documented | The endpoint declares a response the spec doesn't document. |
| MM014 | Warning | Endpoint name is not the operationId | The endpoint's name differs from the spec's operationId, which the description uses. |
| MM015 | Warning | Route parameter named differently | The route's `{parameter}` differs from the spec's. |
| MM016 | Warning | Spec parameter not bound | A spec parameter has no binding. |
| MM017 | Warning | Parameter not in the spec | A bound value the spec doesn't document. |
| MM018 | Warning | Bound with a different type | A parameter bound as another type than the spec's. |
| MM019 | Warning | Optional parameter required | An optional spec parameter bound as non-nullable with no default, which makes it required. |
| MM020 | Warning | Request body not bound as the spec says | The body is missing, extra, of another type, or bound from the wrong place. |
| MM021 | Info | Model consistent, but not by construction | A hand-written model matches its schema today; use the generated type so it stays matched. |
| MM022 | Warning | Model differs from its schema | A hand-written model differs from its schema, at a named path, constraints included. |
| MM023 | Warning | Parameter constraint differs | A parameter's validation attributes differ from the spec's bounds, length or pattern. |

Ids are stable: a retired diagnostic's id isn't reused.
