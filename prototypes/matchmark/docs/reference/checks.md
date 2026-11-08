# Checks

Every check the regression engine (`Matchmark.Testing`) runs, by the name it reports (`Checks.*`). Each was proven by
an injected fault: breaking the thing it guards makes it fail (principle 5).

A finding is a **failure** or a **warning**. A run passes when it has no failures.

## Every response

| Check | Proves |
|:--|:--|
| `NotAServerError` | No 5xx, unless the spec documents that exact status. |
| `StatusDocumented` | The status is documented for the operation. For an invalid request, an undocumented 4xx is only a warning: document how the API rejects bad input. |
| `ContentTypeDocumented` | The content type is one the spec documents for that status, and a response documented without a body has none. |
| `BodyConforms` | The body matches the documented schema. Streamed items (JSON Lines, server-sent events) are checked one by one. |
| `HeadersDocumented` | The response has every header the spec marks required. |

## Requests

| Check | Proves |
|:--|:--|
| `ValidAccepted` | A request that passes every schema isn't rejected (400, 415, 422). |
| `InvalidRejected` | A request that breaks a constraint is rejected with a 4xx. One case per constraint: missing, null, wrong type, too short or long, off its pattern, off its enum, out of range, too big for its integer size, an unknown or missing discriminator, a body that isn't JSON, is missing, or is the wrong content type. |
| `RejectionNamesTheValue` | A validation problem (a body with `errors`) names the value that was wrong. |
| `ReadOnlyIgnored` | A read-only value sent in a request is ignored or refused, never stored. |
| `WriteOnlyHidden` | A write-only value (a password, a secret) never appears in a response. |

## State and links

| Check | Proves |
|:--|:--|
| `ResourceAvailable` | Following a link from a successful response succeeds. |
| `ReadAfterWrite` | Reading back what was just written gives what the write returned. After a 202, it reads without comparing: accepted work changes the resource as it finishes. |
| `DeletedIsGone` | After a successful delete, reading the resource gives 404 or 410. |
| `LocationReadable` | A success's `Location` header can be read, when the spec has an operation that reads it. |

## HTTP semantics

| Check | Proves |
|:--|:--|
| `SafeChangesNothing` | A safe request (GET, HEAD, QUERY) changes nothing ([RFC 9110 §9.2.1](https://www.rfc-editor.org/rfc/rfc9110#section-9.2.1)). |
| `IdempotentTwice` | An idempotent request (PUT, DELETE) twice has the same effect as once ([RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110#section-9.2.2)). |
| `IdempotentReplay` | The same `Idempotency-Key` and body again gives the same answer and creates nothing new. |
| `IdempotencyKeyReuse` | An `Idempotency-Key` reused with a different body is refused (the draft says 422), never applied. |
| `ETagChanges` | A successful conditional change gives a new ETag. |
| `StaleIfMatchRefused` | A change sent with a stale `If-Match` is refused with 412, so no concurrent change is lost. |
| `PagesComplete` | Walking a paged list's cursors repeats nothing, ends, and finds what one big page holds. |
| `OperationCompletes` | Work accepted with 202 reaches a final state when polled. |
| `RetryAfterGiven` | A 429 says when to try again (`Retry-After`). |
| `DeprecationAnnounced` | A deprecated operation announces it with a `Deprecation` header ([RFC 9745](https://www.rfc-editor.org/rfc/rfc9745)), and its `Sunset` isn't earlier. |

## Declared behaviour

These run when the spec, or an overlay on it, declares the behaviour ([extensions](extensions.md)).

| Check | Proves |
|:--|:--|
| `Postcondition` | An operation's `x-matchmark-ensures` (Arazzo criteria over the request and response) hold on each success. |
| `SchemaRule` | Every `x-matchmark-rules` (CEL over `self`) on a schema holds for each instance of it in a response. |
| `TransitionRule` | Every `x-matchmark-transitions` (CEL over `self` and `oldSelf`) holds between two readings of a resource. |
| `RelationHolds` | A metamorphic relation (`x-matchmark-relations`) holds, such as: the first n results are the start of the first n + 1. |

## Workflows and messages

| Check | Proves |
|:--|:--|
| `WorkflowCriteria` | Each step of an Arazzo workflow meets its success criteria, or its failure is one the workflow handles. |
| `MessageConforms` | A message in a workflow, sent by it or received from the API, conforms to its AsyncAPI message. |

## Proposed

From the [invariants across a collection](invariants.md), each proven by an injected fault before it counts:

| Check | Proves |
|:--|:--|
| `UniqueEnforced` | A write that repeats another resource's unique key is refused with the documented status; a fresh key is accepted. |
| `ReferenceHolds` | A reference to nothing is refused; a delete applies its `onDelete` (restrict, cascade, clear). |
| `DerivedHolds` | A derived value equals its expression in every response that holds the resource. |
| `TotalConserved` | A conserved total over the collection is the same before and after each operation that must keep it. |
| `ScopeHolds` | Another principal can't list, read, change or delete a scoped resource. |
| `LimitEnforced` | The create past a limit is refused with the documented status. |
| `ListOrdered` | A list is in its declared order, page after page. |

## Sessions

A hosted session's report ([sessions](sessions.md#the-report)) isn't a check of the engine's, but it judges the same way:
a required pathway not completed, an invalid call, or a provider that doesn't pass the pathways each make the run
incomplete, with a reason.
