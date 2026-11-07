# Layers: behaviour beyond the OpenAPI document

An OpenAPI document describes requests and responses one at a time. Real APIs also have behaviour *across* calls and
over time: a create that can be retried safely, an update that refuses to overwrite a change it didn't see, a list that
comes in pages, work that finishes later, limits, deprecation, events. Other specifications and HTTP standards
describe that behaviour. This document is how Matchmark takes them on. [VISION.md](VISION.md) says why,
[GAPS.md](GAPS.md) where the prototype falls short, and [samples/](samples/README.md) is the corpus each layer is proven
against.

## The rule: standard first, annotate only the remainder

1. **Infer from what the spec already says in standard HTTP.** An `Idempotency-Key` header parameter *is* the
   declaration that an operation can be retried safely. An `ETag` response header and an `If-Match` request header *are*
   optimistic concurrency. A `202` with `Location` *is* work that finishes later. `deprecated: true` with `Deprecation`
   and `Sunset` headers *is* a deprecation. A spec written well for people already declares most behaviour.
2. **Use the sibling specifications for what one operation can't say.** [Arazzo](https://spec.openapis.org/arazzo/latest.html)
   for sequences of calls, with success criteria and values passed between steps. [Overlay](https://spec.openapis.org/overlay/latest.html)
   for adding to a spec without editing it. [OpenAPI 3.2](https://www.openapis.org/blog/2025/09/23/announcing-openapi-v3-2)
   for new methods and streams. [AsyncAPI](https://www.asyncapi.com/) for events.
3. **`x-matchmark-*` only for what neither says** (how long an idempotency key is remembered, where a cursor comes
   from), and preferably in an Overlay, so the published spec stays clean and a third-party spec needn't be touched.
4. **Learn the vocabulary from those who've done it.** SDK generators already model this behaviour as extensions
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
| **Workflows** | Arazzo 1.0/1.1 | each step's success criteria; outputs passed on; `retry` and `goto` on failure | the fake scripted from a workflow | Museum and Orders ship one; nothing runs them |
| **Idempotency** | an `Idempotency-Key` header parameter ([draft-ietf-httpapi-idempotency-key-header](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/)); Overlay for the window | same key and body gives the same answer; same key, different body gives 422; a fresh key per case otherwise | a key store; a client that retries with the same key | Orders; the engine **wrongly** sends one sampled key everywhere |
| **Concurrency** | `ETag` response header plus `If-Match` request header ([RFC 9110 §13](https://www.rfc-editor.org/rfc/rfc9110#section-13)); 412, 428 ([RFC 6585](https://www.rfc-editor.org/rfc/rfc6585)) | a stale ETag gives 412; none gives 428; a change gives a new ETag | ETags from a version; the client sends `If-Match` | Orders |
| **Pagination** | a cursor parameter plus a `next` in the body, or `Link: rel=next` ([RFC 8288](https://www.rfc-editor.org/rfc/rfc8288)); Overlay to say which | walking every page is complete, with no repeats, and ends | the client yields every item | Orders |
| **Work that finishes later** | `202` with `Location`; `Retry-After` on the status | polling reaches a final state within a bound; never a 5xx while running | the client polls until done | Orders |
| **Rate limits** | `429` with `Retry-After`; RateLimit headers ([draft](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/)) | a 429 carries `Retry-After`; the suite backs off rather than failing | the client waits and retries; the fake can be told to limit | Orders |
| **Deprecation** | `deprecated: true`; `Deprecation` ([RFC 9745](https://www.rfc-editor.org/rfc/rfc9745)) and `Sunset` ([RFC 8594](https://www.rfc-editor.org/rfc/rfc8594)) headers | the headers are present, and Sunset isn't before Deprecation | the client warns; generated code is `[Obsolete]` | Orders |
| **Events** | OpenAPI `webhooks` and `callbacks`; AsyncAPI 3 (which Arazzo 1.1 can sequence) | the receiver accepts each documented event; the sender's payloads conform | a typed receiver; the fake can send events | Orders declares one; both **silently ignore** it |
| **New methods and streams** | OpenAPI 3.2 `QUERY` and `additionalOperations`; `itemSchema` for `text/event-stream` and `application/jsonl` | each streamed item conforms, as it arrives | typed streams (`IAsyncEnumerable<T>`) | not started; needs a reader that understands 3.2 |

## What the corpus found

Running the generator and the engine over specs other than the Pet Store found these:

- **The engine crashed on its first other spec.** Numbers it builds in memory (`12345`, an `int`) can't be read back
  as `decimal` by System.Text.Json; only numbers parsed from text can. The Museum API's string parameters triggered it.
  Fixed: every number is now read through one helper. `CorpusTests` fails if it comes back.
- **The generator's subset is narrow for real specs.** It skips 29 things in the Museum API. Primitive component
  schemas (`EventId`, `Date`, `Email`), arrays as named components, and `allOf` are all common in real specs.
- **The engine is already broad.** It needs no generated types, so it builds 100 cases over all eight Museum
  operations. It finds **no workflows**, though, because the Museum API has no `links`, yet ships an Arazzo document
  describing exactly the create, read, update and delete flow. That is the case for Arazzo.
- **Behaviour must be modelled, not just declared.** Against a correct Orders server the suite would send the same
  sampled `Idempotency-Key` with different bodies, get the 422 the draft requires, and report a *valid request
  rejected*. A suite that doesn't know idempotency gets it backwards.
- **The status table is too small.** The Orders API's 412 and 429 are skipped by the generator.
- **Webhooks are silently ignored** by both the generator and the engine, so they should at least be reported.
- **Strict reading turns away real specs.** Bump.sh's Train Travel API (not vendored: its licence isn't stated) is
  rejected outright: Microsoft.OpenApi reports an `xml` keyword it doesn't expect as an error, the generator treats
  every reader error as fatal, and the CLI crashes with an unhandled exception instead of reporting it.

## The plan, in order

Each step is done when it is declared in a sample, it is in the ledger, and its checks are proven by an injected fault.

1. **Breadth and robustness.** The generator learns primitive and array component schemas, `allOf` and inline objects;
   any status code; reader errors that aren't fatal reported as warnings; webhooks and anything unsupported reported,
   never silently dropped; a CLI that reports instead of crashing. The ledger measures it: Museum's 29 skips go toward 0,
   and Train Travel becomes readable.
2. **Arazzo in the engine.** It reads workflows next to the spec and runs them as regression workflows: success
   criteria, outputs, runtime expressions, `retry` and `goto`. The same workflow can script the fake. Museum's and
   Orders' workflows become runnable cases. This is the biggest gain for the least code, and it works against any server.
3. **Behaviour in the model, inferred by the rule above.** Each behaviour lands in the engine first: idempotency,
   concurrency, pagination, work that finishes later, rate limits and deprecation, each with its checks and a fault
   that proves them. Then the generated client, the fake and the server, one behaviour at a time, starting with
   idempotency and concurrency on Orders. This is the semantic model of VISION.md's stage 2, with behaviour as traits on
   operations.
4. **Overlays at build time.** The spec plus its overlays becomes the effective document that the generator and engine
   read. This is how behaviour attaches to an API you don't own, which the twins in GAPS.md need.
5. **OpenAPI 3.2 and events.** `QUERY`, streamed items and webhooks, then AsyncAPI. This waits on a reader that
   understands 3.2.

## Versions this is written against

OpenAPI 3.1, with 3.2 released in September 2025. Arazzo 1.0.1, with 1.1 (May 2026) adding AsyncAPI steps. Overlay
1.0, with 1.1 released in 2026. The Idempotency-Key and RateLimit headers are IETF working-group drafts (as of late 2025,
drafts -07 and -10). RFC 9457, 9110, 8288, 6585, 9745 and 8594 are published.
