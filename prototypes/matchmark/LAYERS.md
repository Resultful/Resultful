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
| **Events** | OpenAPI `webhooks` and `callbacks`; AsyncAPI 3 (which Arazzo 1.1 can sequence) | the receiver accepts each documented event; the sender's payloads conform | a typed receiver; the fake can send events | ✓ Webhooks: generated as `{Api}Webhooks` (a receiver interface and `Map{Api}Webhooks`, a sender client, a fake), and `RegressionSuite.ForWebhooks` (`regress --webhooks`) tests a receiver (`WebhookTests`). ✓ Callbacks, generated the same way as `{Api}Callbacks` (`RegressionSuite.ForCallbacks`; Notifications). ✓ AsyncAPI 3, in the engine: `AsyncApiDocument` reads operations and their messages through `$ref`s; its `receive` operations become a suite run against a consumer through an HTTP bridge (`POST /{operationId}`, payload as body, message headers as headers), and what a `send` operation publishes is checked message by message (`Check`); `matchmark regress` takes an AsyncAPI document (Notifications' `asyncapi.yaml`, `AsyncApiTests`). ✓ Generated: the generator reads an AsyncAPI 3 document as webhooks, one per operation, so each payload is a typed model and each operation gets a receiver interface and a sender (`AsyncApiGenerationTests`). ✓ Arazzo 1.1 message steps (`action: send`/`receive`, `correlationId`, `timeout`, `$message`) run through the bridge, each message checked (`MessageConforms`; `notifications.arazzo.yaml`) |
| **New methods and streams** | OpenAPI 3.2 `query` operations (the QUERY method, [RFC 10008](https://www.rfc-editor.org/rfc/rfc10008)) and `additionalOperations`; `in: querystring`; `itemSchema` for `text/event-stream` and `application/jsonl` | each streamed item conforms, as it arrives | typed streams (`IAsyncEnumerable<T>`) | ✓ `query` operations everywhere (a 3.2 spec is kept as 3.2), `additionalOperations` in the engine; ✓ JSON Lines as `IAsyncEnumerable<T>` (endpoint, client, fake, scaffold), and every streamed item checked by the engine (Catalogue); ✓ server-sent events too, each event's data typed by its `contentSchema` and checked against it (Notifications); ✓ `in: querystring`: the whole query as one typed object, read and validated by the endpoint, written by the client, sent and broken leaf by leaf by the engine, checked by the twin (Catalogue's `findProducts`) |

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

- **The first run over GitHub's and Stripe's specs** (1,272 and 612 operations) found the generator failing outright,
  then code that didn't compile. A `oneOf` beside an object's properties without a discriminator ("one of these
  fields is required") was taken for a union; a property named like its type (`Email.email`) and properties whose
  names differ only in symbols (GitHub's reactions `+1` and `-1`) made invalid C#; and an API with no operation the
  generator could write got a client declared with no interfaces. *Fixed:* such a `oneOf` only constrains the object;
  property names spell out a leading `+`/`-`, take `Value` when they'd repeat the type's name, and are numbered when
  they'd repeat a sibling's. GitHub's spec now generates 95% of its operations, and all of it compiles.

- **Stripe found the next three.** Form bodies (`application/x-www-form-urlencoded`, nested values in brackets) and the
  `default` response weren't generated, so none of Stripe's 612 operations was; and the Payments sample written for
  them found that arrays were never validated (`maxItems`, and each item's own constraints). *Done:* forms are read
  and written by the generated server, client and engine; `default` is an error case that carries its status; arrays
  are checked. Stripe went from 0% to 94%; with deep-object query parameters (`created[gte]=…`, read and validated by
  the endpoint, written by the client, sent by the engine with a case for each leaf's bounds) and schemas that are any
  JSON value typed rather than skipped, it's at 99.8%.

- **Notifications found four.** `format: uri` wasn't checked, so a bad callback URL was stored and failed later; enum
  parameters were bound by `Enum.TryParse`, which refused the spec's own names and took a number; the client wrote
  enum and date parameters with `ToString`, not as the spec writes them; and a missing header was named by its C#
  parameter (`xSignature`), not the spec's (`X-Signature`). *Fixed:* `uri` and `email` formats are validated; enum
  parameters are read by their JSON names; the client writes parameters as the spec does; binding errors use the
  spec's names.

- **Flags found that a handler couldn't set a response header**, so an ETag the spec requires could never be sent.
  *Fixed:* a handler sets the headers the spec documents through `{Api}Headers` (`FlagsHeaders.SetETag(…)`), a scope the
  generated endpoint (and scaffolded controller) opens around each call and writes onto the response; the client keeps
  the last response's headers (`LastHeaders`), to send an ETag back as If-Match. Its suite also caught the sample's own
  handler bumping a version on a PUT that changed nothing, which RFC 9110's idempotent PUT doesn't allow.

- **Ledger found two in the engine.** When every sampled request broke a declared rule (a transfer from an account to
  itself), no valid case was left to start the links workflow from, and building the suite threw; it's a gap now, and
  an example is the fix it names. And after a 202 the engine compared what it read back with what was written, though
  accepted work changes the resource as it finishes (pending, then settled); it now reads after a 202 without
  comparing, and the transition rule says how the resource may change.

- **Catalogue's second round found three gaps a real spec has.** A response keyed `4XX` or `5XX` (a range, allowed
  since OpenAPI 3.0) was skipped with its operation; a `text/plain` or `text/markdown` request body was skipped too
  (GitHub's `/markdown/raw`); and OpenAPI 3.2's `in: querystring` wasn't read. *Done:* a range is an error case that
  carries its status (`ClientError(int status, …)`), matched after every exact status and before `default`; a text body
  is a `string`, read whole by the endpoint, checked against its schema's lengths and pattern (an empty required body
  is missing), refused with 415 when sent as anything else, and sent by the client and engine as text; a querystring
  parameter is the whole query as one object. Running the generated server against the suite caught the generator's
  own first attempt: it accepted text shorter than `minLength` and an empty required body.

- **AsyncAPI found one more.** Its generated consumer took a notification on a topic its inline `enum` doesn't list:
  an enum written inline on a string property stayed a string and wasn't checked. *Fixed:* its values are checked.

- **The fidelity loop, run with an agent in it** (`AgentLoopTests`, Ledger, 30% held out). The writer (this
  prototype's own coding agent, as no model API is configured for CI) saw only the training tasks. Attempt 1 kept
  accounts as the real ledger does (`{id, owner, balance}`, the opening amount as the balance): held out rose from
  61.9% to 81.0%, but `getTransfer` fell to 50%, so it was rejected; the twin's own store no longer had the accounts
  transfers refer to. Attempt 2 kept that store in step (`SpecTwin.Seed` from the behaviour): 85.7%, nothing fell,
  kept. The last training tasks were the real API's empty-bodied 415, which owned behaviour couldn't reach, as the
  twin rejects before behaviours run; `SpecTwin.OnRejected` is that seam now. Attempt 3 used it: training reached
  100% but held out didn't rise, so it was rejected, which is the gate refusing a change it can't see pay off.
  Then an independent agent (a separate session, given only `LedgerOwned.cs` to edit, the gate's training tasks and
  held-out score, the spec and the twin's API, and barred from the real server's code, these attempts and the docs)
  ran the loop from an empty `LedgerOwned`. Its first attempt rewrote the opening into the balance as the twin
  stores an account, and answered 415 with no body: training 100%, held out 61.9% to 85.7%, kept. Four guesses about
  transfers (409 on insufficient funds, 404 on unknown accounts, balances moving) were kept by the gate but moved
  nothing, and one (empty 404s) was rejected because `getTransfer` fell to 0%; it went back to its first attempt as the
  one with measured gain. It relied on a behaviour changing the request body and stored resources, which `TwinCall`
  now documents. `The_owned_behaviour_passes_the_gate` holds what it wrote. The loop runs unattended in
  `.github/workflows/agent-loop.yml` once the repository has an `ANTHROPIC_API_KEY` secret.

- **The support matrix found two in the twin.** Run over one document per format version (samples/versions), the
  twin answered a 404 the spec documents without a body with a problem body anyway, and answered an operation it
  couldn't map to a collection (OpenAPI 3.2's `QUERY` search) with a sampled body missing its read-only `id`, because
  it sampled the response as if it were a request. *Fixed:* a status documented without a body is sent without one,
  and response samples include read-only values and leave out write-only ones.

- **A declaration replaced owned code.** The Pet Store's unique name was hand-written in the twin's owned code. Declared
  once (`x-matchmark-unique`), the twin from the spec alone scores what the owned code did (93.2%), the suite checks
  the real service refuses duplicates, case variants and renames into a taken name, and the audit suggests the
  declaration where a create documents 409. Its first run flagged the Orders sample too, wrongly: a create that takes an
  Idempotency-Key documents 409 for a request still being processed, so the rule now leaves those out.

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
   idempotency and concurrency on Orders. This is the semantic model of VISION.md's roadmap step 2, with behaviour as traits on
   operations.
4. ✓ **Overlays at build time.** The spec plus its overlays becomes the effective document that the generator and engine
   read. This is how behaviour attaches to an API you don't own, which the twins in GAPS.md need. Overlay 1.0 to 1.2:
   `update` (objects merged, arrays appended), `remove`, and `copy`; targets are JSONPath with filters. An overlay is a
   `MatchmarkOverlay="true"` item next to the spec, or `--overlay` to `scaffold` and `regress`. The invariants it carries
   (COMPLETENESS.md) are checked by the engine.
5. **OpenAPI 3.2 and events.** ✓ Webhooks, ✓ callbacks, ✓ `QUERY`, ✓ streamed items (JSON Lines and server-sent
   events), ✓ `in: querystring`, ✓ AsyncAPI 3: a consumer's suite over a bridge, a producer's messages checked, typed
   messages generated, and Arazzo 1.1 message steps run.

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
