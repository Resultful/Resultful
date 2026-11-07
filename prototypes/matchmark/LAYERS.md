# Layers: behaviour beyond the OpenAPI document

An OpenAPI document describes requests and responses one at a time. Real APIs also have behaviour *across* calls and
over time: a create that can be retried safely, an update that refuses to overwrite a change it didn't see, a list that
comes in pages, work that finishes later, limits, deprecation, events. Other specifications and HTTP standards
describe that behaviour. This document is how Matchmark takes them on. [VISION.md](VISION.md) says why,
[GAPS.md](GAPS.md) where the prototype falls short, and [samples/](samples/README.md) is the corpus each layer is proven
against. [COMPLETENESS.md](COMPLETENESS.md) covers the other half: encouraging a spec complete enough to test, and
stating the invariants a schema can't.

## The rule: standard first, annotate only the remainder

1. **Infer from what the spec already says in standard HTTP.** An `Idempotency-Key` header parameter *is* the
   declaration that an operation can be retried safely. An `ETag` response header and an `If-Match` request header *are*
   optimistic concurrency. A `202` with `Location` *is* work that finishes later. `deprecated: true` with `Deprecation`
   and `Sunset` headers *is* a deprecation. A spec written well for people already declares most behaviour.
2. **Use the sibling specifications for what one operation can't say.** [Arazzo](https://spec.openapis.org/arazzo/v1.1.0.html)
   for sequences of calls, with success criteria and values passed between steps (and, since 1.1, AsyncAPI messages).
   [Overlay](https://spec.openapis.org/overlay/v1.2.0.html) for adding to a spec without editing it.
   [OpenAPI 3.2](https://spec.openapis.org/oas/v3.2.1.html) for new methods and streams. [AsyncAPI](https://www.asyncapi.com/)
   for events.
3. **Name behaviour as traits, and apply them with Overlays.** Overlay 1.2.0's own *Traits Example* marks operations
   with `x-oai-traits: ['paged']`, and an overlay then targets every operation carrying a trait
   (`$.paths[?(@.get['x-oai-traits'][?(@ == 'paged')])].get`), not each path by hand. That is the shape Matchmark
   wants: a trait named once in the spec, with its detail in an overlay that a third-party spec needn't contain.
   `x-oai-traits` is proposed for the OAI extension registry but not yet registered.
4. **`x-matchmark-*` only for what neither says** (how long an idempotency key is remembered, where a cursor comes
   from), carried in an Overlay. The OAI keeps a [namespace registry](https://spec.openapis.org/registry/namespace/)
   (`oai`, `ms`, `sap`, `scalar`, …); registering `matchmark` reserves the `x-matchmark-` prefix against collisions.
5. **Learn the vocabulary from those who've done it.** SDK generators already model this behaviour as extensions
   (`x-speakeasy-pagination`, `x-speakeasy-retries`, Azure's `x-ms-pageable` and `x-ms-long-running-operation`),
   Smithy as traits (`@idempotencyToken`, `@paginated`, `@retryable`) and TypeSpec as decorators. Matchmark can read
   those as aliases rather than invent a rival vocabulary.

## What "supported" means

A behaviour is supported when it reaches every place the spec already reaches. Declaring it once must make it true
in each of these places:

| Where | What it means for a behaviour |
|:--|:--|
| **Server** (generated, or verified if owned) | the service does it: stores idempotency keys, issues ETags, refuses stale ones |
| **Client** | the client uses it: retries with the same key, sends `If-Match`, follows pages, polls |
| **Fake** | the fake behaves the same way, so consumers can test against it |
| **Regression suite** | a check that any server, in any language, does it, proven by an injected fault |

The regression suite comes first for each behaviour. It is the cheapest piece, it works against any server, and it
defines the behaviour precisely enough to generate the other three.

## The layers

| Layer | Declared by | Regression checks | Server, client, fake | Today |
|:--|:--|:--|:--|:--|
| **Problems** | [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) `application/problem+json` | content type, schema, a validation problem names the value | generated | ✓ done |
| **Links** | OpenAPI `links` | the resource is there, reads back as written, is gone after delete | regression only | ✓ done |
| **Workflows** | Arazzo 1.0 and 1.1 (1.1 adds RFC 9535 JSONPath, JSON Pointer selectors and AsyncAPI steps) | each step's success criteria; outputs passed on; `retry` and `goto` on failure | the fake scripted from a workflow | ✓ run by the engine (`WorkflowCriteria`); ✓ scripts the fake (`Scenario.FromArazzo`); XPath and AsyncAPI steps are gaps |
| **Idempotency** | an `Idempotency-Key` header parameter ([draft-ietf-httpapi-idempotency-key-header](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/)); Overlay for the window | same key and body gives the same answer; same key, different body gives 422; a fresh key per case otherwise | a key store; a client that retries with the same key | Orders; a fresh key per case, and the same key when retrying |
| **Concurrency** | `ETag` response header plus `If-Match` request header ([RFC 9110 §13](https://www.rfc-editor.org/rfc/rfc9110#section-13)); 412, 428 ([RFC 6585](https://www.rfc-editor.org/rfc/rfc6585)) | a stale ETag gives 412; none gives 428; a change gives a new ETag | ETags from a version; the client sends `If-Match` | Orders |
| **Pagination** | a cursor parameter plus a `next` in the body, or `Link: rel=next` ([RFC 8288](https://www.rfc-editor.org/rfc/rfc8288)); Overlay to say which | walking every page is complete, with no repeats, and ends | the client yields every item | Orders |
| **Work that finishes later** | `202` with `Location`; `Retry-After` on the status | polling reaches a final state within a bound; never a 5xx while running | the client polls until done | Orders |
| **Rate limits** | `429` with `Retry-After`; RateLimit headers ([draft](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/)) | a 429 carries `Retry-After`; the suite backs off rather than failing | the client waits and retries; the fake can be told to limit | Orders |
| **Deprecation** | `deprecated: true`; `Deprecation` ([RFC 9745](https://www.rfc-editor.org/rfc/rfc9745)) and `Sunset` ([RFC 8594](https://www.rfc-editor.org/rfc/rfc8594)) headers | the headers are present, and Sunset isn't before Deprecation | the client warns; generated code is `[Obsolete]` | Orders |
| **Events** | OpenAPI `webhooks` and `callbacks`; AsyncAPI 3 (which Arazzo 1.1 can sequence) | the receiver accepts each documented event; the sender's payloads conform | a typed receiver; the fake can send events | ✓ Webhooks: generated as `{Api}Webhooks` (a receiver interface and `Map{Api}Webhooks`, a sender client, a fake), and `RegressionSuite.ForWebhooks` (`regress --webhooks`) tests a receiver (`WebhookTests`). Callbacks and AsyncAPI are next |
| **New methods and streams** | OpenAPI 3.2 `query` operations (the QUERY method, [RFC 10008](https://www.rfc-editor.org/rfc/rfc10008)) and `additionalOperations`; `in: querystring`; `itemSchema` for `text/event-stream` and `application/jsonl` | each streamed item conforms, as it arrives | typed streams (`IAsyncEnumerable<T>`) | not started; needs a reader that understands 3.2 |

## What the corpus found

Running the generator and the engine over specs other than the Pet Store found these:

- **The engine crashed on its first other spec.** Numbers it builds in memory (`12345`, an `int`) can't be read back
  as `decimal` by System.Text.Json; only numbers parsed from text can. The Museum API's string parameters triggered it.
  Fixed: every number is now read through one helper. `CorpusTests` fails if it comes back.
- **The generator's subset was narrow for real specs.** It skipped 29 things in the Museum API: primitive component
  schemas (`EventId`, `Date`, `Email`), arrays as named components, and `allOf`, all common in real specs. *Fixed:*
  aliases, flattened `allOf`, inline objects, any status and `IResult` past six responses. Museum is down to its
  webhook and three advisory warnings, and every sample's generated code compiles.
- **The engine is already broad.** It needs no generated types, so it builds 100 cases over all eight Museum
  operations. It finds **no workflows**, though, because the Museum API has no `links`, yet ships an Arazzo document
  describing exactly the create, read, update and delete flow. That is the case for Arazzo.
- **Behaviour must be modelled, not just declared.** Against a correct Orders server the suite would send the same
  sampled `Idempotency-Key` with different bodies, get the 422 the draft requires, and report a *valid request
  rejected*. A suite that doesn't know idempotency gets it backwards.
- **The status table was too small.** The Orders API's 412 and 429 were skipped. *Fixed:* any standard status.
- **Webhooks were silently ignored.** *Now reported* by the generator; generating them is still to do.
- **Strict reading turned away real specs.** Bump.sh's Train Travel API (not vendored: its licence isn't stated) is
  OpenAPI 3.2, which deprecates `xml.wrapped` but still allows it. Microsoft.OpenApi 3.11 reports it as an error. The
  generator treated every reader error as fatal, and the CLI crashed with an unhandled exception. *Fixed:* one lenient
  reader (`SpecReader`) uses whatever document the reader builds and reports its objections as warnings (`MM007`); the
  CLI reports errors and exits 2. The Train Travel API now gets 116 regression cases, and the generator's remaining
  warnings are the breadth gaps below.

- **The Bookings sample found three generator gaps** the Pet Store never reached, once its generated server was run
  against the suite (`GeneratedRulesTests`). Bound query parameters weren't checked against their `minimum` and
  `maximum` (`roomId=0` was accepted). An optional property that can't be null was written as `null` when absent, and
  an explicit `null` was accepted. And `additionalProperties: false` wasn't enforced. *Fixed:* endpoints check their
  parameters' bounds, lengths and patterns; such properties are left out when absent, and a `null` is refused when read
  (a resolver modifier, `RefuseNulls`, so the OpenAPI document's schemas are untouched); and the model refuses unknown
  members, naming them.

- **Webhooks found two more.** Generated enums took a number as a member (`JsonStringEnumConverter` allows integers
  by default), so `status: 3` was accepted; and a `202` with no body called `TypedResults.Accepted()`, which needs a
  URI. *Fixed:* enums use `{Api}EnumConverter<T>`, which refuses numbers; the 202 passes a null location.

## The plan, in order

Each step is done when it is declared in a sample, it is in the ledger, and its checks are proven by an injected fault.

1. ✓ **Breadth and robustness.** Primitive and array component schemas, `allOf`, inline objects, any status code,
   webhooks reported; reader errors that leave a document are warnings, and the CLI reports instead of crashing. The ledger measures it: Museum's 29 skips go toward 0,
   and Train Travel becomes readable.
2. ✓ **Arazzo in the engine.** It reads workflows next to the spec and runs them as regression workflows: success
   criteria (`simple`, `regex`, and a JSONPath subset), outputs, runtime expressions, `{$…}` interpolation,
   `operationPath`, workflows in other documents, `retry`, `goto` and `end`. The Pet Store's workflow runs against the
   service; every workflow in the corpus is runnable. ✓ The same workflow scripts the fake
   (`Scenario.FromArazzo`): a consumer's client is tested against the workflow the service is regressed with.
3. **Behaviour in the model, inferred by the rule above.** ✓ In the engine: idempotency, concurrency, paging, work that
   finishes later, rate limits and deprecation (`OrdersTests`). Each behaviour lands in the engine first: idempotency,
   concurrency, pagination, work that finishes later, rate limits and deprecation, each with its checks and a fault
   that proves them. Then the generated client, the fake and the server, one behaviour at a time, starting with
   idempotency and concurrency on Orders. This is the semantic model of VISION.md's stage 2, with behaviour as traits on
   operations.
4. ✓ **Overlays at build time.** The spec plus its overlays becomes the effective document that the generator and engine
   read. This is how behaviour attaches to an API you don't own, which the twins in GAPS.md need. Overlay 1.0 to 1.2:
   `update` (objects merged, arrays appended), `remove`, and `copy`; targets are JSONPath with filters. An overlay is a
   `MatchmarkOverlay="true"` item next to the spec, or `--overlay` to `scaffold` and `regress`. The invariants it carries
   (COMPLETENESS.md) are checked by the engine.
5. **OpenAPI 3.2 and events.** ✓ Webhooks. `QUERY`, streamed items and callbacks, then AsyncAPI. This waits on a reader that
   understands 3.2.

## Versions this is written against

Read from the OAI's published specifications (the source of spec.openapis.org, `OAI/spec.openapis.org` at commit
`99da701`, November 2026):

- **OpenAPI 3.2.1.** 3.2.0 was released in September 2025. It adds `query` operations for the QUERY method, now
  RFC 10008, plus `additionalOperations`, `in: querystring` parameters, and `itemSchema` for sequential media types.
- **Arazzo 1.1.0.** It adds AsyncAPI steps (`channelPath`, `correlationId`, `action: receive`, `$message`) and `$self`.
  Its criteria take JSONPath per RFC 9535 by default, with the Goessner draft still allowed, and JSON Pointer
  (RFC 6901) as a selector.
- **Overlay 1.2.0.** It adds reusable actions and `copy`, with RFC 9535 JSONPath targets and the traits pattern above.
- **The extension registry.** `x-oai-traits` is proposed there but not registered. The namespace registry is where
  `matchmark` would go.

The Idempotency-Key and RateLimit headers are IETF working-group drafts (as of late 2025, drafts -07 and -10).
RFC 9457, 9110, 8288, 6585, 9745, 8594 and 10008 are published.
