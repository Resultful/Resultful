# A spec complete enough to test

**A proposal.** Nothing here is built yet. [LAYERS.md](LAYERS.md) covers behaviour across calls; this covers the other
half of making the spec a better oracle. One part is a report that encourages a team to write a complete spec. The other
is a way to state invariants, the rules a schema can't express, in grammars that already exist.

## Why

The regression suite can only check what the spec says. The Pet Store passes with 11 warnings because its spec doesn't
say how three operations reject a malformed parameter. A string without `maxLength` gets no boundary cases. A create
without `links` gets no workflow. And no schema can say that an order's total is the sum of its lines. Every gap in the
spec is a test that can't be generated.

So Matchmark should do two things:
- **Show what's missing.** For each gap, report what it costs in tests.
- **Let the spec say more.** Give the rules a schema can't express a grammar, using one that others already use.

## 1. The completeness report: `matchmark audit`

A report of what the spec leaves out. Linters exist for API style ([Spectral](https://github.com/stoplightio/spectral)
rulesets, [vacuum](https://github.com/daveshanley/vacuum), [Zally](https://github.com/zalando/zally)), so this doesn't
compete on style. Its difference is that every finding is **measured in tests**. Matchmark generates the suite, so it
can say exactly what each fix would add:

```
createPet  body.name has no maxLength
           → no boundary case, no "too long" case, and no limit on what the server stores.
             Adding maxLength adds 2 cases.
getPet     no response documents how a malformed petId is rejected (the suite saw 400)
           → 3 cases end in a warning, not a check. Document the 400.
createPet  201 has no links and no Arazzo workflow reaches it
           → nothing checks a created pet can be read back, changed or deleted.
Score: 81% of what the suite could check is checkable. The changes above would add 14 cases and 1 workflow.
```

The rules, each with what it unlocks:

| Rule | What it unlocks |
|:--|:--|
| every operation has an `operationId` | the operation is in the contract at all |
| every string has `maxLength`, `pattern`, `format` or `enum`; every number has bounds | boundary and out-of-range cases, and a server that can't be fed unbounded input |
| every way a request can be rejected is documented: a 400 for operations with a body or parameters, a 404 for operations on one resource | rejections are checked, not warned about |
| examples on request bodies and success responses | example cases; a fake that answers realistically; scaffolded reference answers |
| `readOnly` on server-assigned fields (`id`, `createdAt`) and `writeOnly` on secrets | the "a read-only field sent is ignored or refused" invariant; leak checks on responses |
| `additionalProperties` stated on every object | the suite knows whether an extra field is a bug |
| `links` from every create, or an Arazzo workflow | read-after-write, update, delete and deleted-is-gone |
| security schemes, with 401 and 403 documented | unauthenticated and unauthorised cases |
| standard behaviour declared where it applies (an `Idempotency-Key` on unsafe creates, `ETag` with `If-Match` on updates) | the behaviour checks in LAYERS.md |
| invariants (section 2) where the schema can't say it | cross-field, postcondition, lifecycle and metamorphic checks |

**How it ships:**
- the CLI (`matchmark audit spec.yaml`), with SARIF output so CI and editors can show it
- an analyzer diagnostic for each finding, in projects that generate the contract
- the rules exported as a Spectral-compatible ruleset, so teams can keep their existing linter for the static part

Only Matchmark can measure what each fix is worth, because only it generates the suite.

## 2. Invariants, in grammars that already exist

An invariant is a rule the schema can't express. There are four scopes, and an established grammar for each. Matchmark
reads them rather than inventing one.

| Scope | Example | Grammar | Prior art |
|:--|:--|:--|:--|
| **One payload:** fields of one body | `endsAt > startsAt`; `total == items.map(i, i.quantity * i.unitPrice).sum()` | [CEL](https://cel.dev/), as `x-matchmark-rules`, following Kubernetes' `x-kubernetes-validations` (`rule`, `message`) | Kubernetes validates CRDs (OpenAPI schemas) with CEL rules. JSON Schema's `dependentRequired` and `if`/`then` cover the simpler cases first |
| **Parameters:** inputs of one request | `OnlyOne(roomId, deskId)`; `IF sort THEN order`; `from <= to` | [IDL](https://github.com/isa-group/IDL), as `x-dependencies` on the operation (its own OpenAPI extension) | IDL and IDLReasoner (Martin-López, Segura et al., University of Seville). The authors report IDL helped find more than 200 bugs in industrial APIs, GitHub, Spotify and YouTube among them |
| **Request to response:** postconditions | the created order has the lines sent; every result of `?status=paid` is paid; at most `limit` items | Arazzo's [Criterion Object](https://spec.openapis.org/arazzo/v1.1.0.html): simple conditions over runtime expressions (`$request.body#/…`, `$response.body#/…`), or JSONPath (RFC 9535 by default since Arazzo 1.1) | Arazzo `successCriteria`. Matchmark's engine already evaluates runtime expressions for links |
| **Across calls:** state and time | GET changes nothing; PUT twice is PUT once; `shipped` only from `paid`; `?limit=2` is a prefix of `?limit=3` | HTTP's own semantics; CEL transition rules with `oldSelf`; metamorphic relations | [RFC 9110 §9.2](https://www.rfc-editor.org/rfc/rfc9110#section-9.2) (safe and idempotent methods); Kubernetes transition rules; *Metamorphic Testing of RESTful Web APIs* (Segura et al., IEEE TSE 2018), which found 11 issues in Spotify and YouTube |

**Some invariants need declaring by no one.** HTTP already promises them, so Matchmark can check them on every API:

- A safe method (`GET`, `HEAD`, `QUERY`) changes nothing: the collection reads the same before and after.
- An idempotent method (`PUT`, `DELETE`) twice has the same effect as once.
- A `201` with `Location` points at something a `GET` can read.
- `readOnly` fields sent in a request are ignored or refused, never stored.
- `writeOnly` fields never appear in a response.

**Declared invariants go in all four places**, as in LAYERS.md:

| Where | What an invariant becomes |
|:--|:--|
| Server | generated validation: a broken cross-field or parameter rule is the spec's 400, naming the fields |
| Client | the same check before sending, so a bad request never leaves |
| Fake | data built to satisfy the rules (IDLReasoner treats dependencies as a constraint problem); lifecycle transitions enforced |
| Regression suite | valid cases satisfy every rule, and each invalid case breaks exactly one; postconditions checked on every response; transitions checked in workflows; each metamorphic relation run as a pair of requests |

**Choosing the grammars.** Each was picked because it is already published, implemented and used on OpenAPI or JSON:

- **CEL** is used on OpenAPI schemas by Kubernetes, and has `oldSelf` for transitions. A .NET evaluator needs choosing or
  writing; a subset (comparisons, arithmetic, `all`, `exists`, `map`, `sum`) covers the examples here.
- **IDL** is the only published grammar for parameter dependencies, with a reasoner and real-world results.
- **Arazzo criteria** are already in the OpenAPI family, so a workflow's `successCriteria` and an operation's
  postconditions share one grammar. Since Arazzo 1.1 that includes standard JSONPath (RFC 9535) and JSON Pointer.
- **Where they live:** markers in the spec as traits (`x-oai-traits`, as in Overlay 1.2.0's traits example), and the
  rules themselves in an overlay under a registered `x-matchmark-` namespace, so a third-party spec needn't change.

Considered and not chosen as the main grammar:
- **JSON Logic** and **JMESPath:** implementable, but nobody uses them on OpenAPI.
- **OCL**, UML's invariant language: the right idea, but not used on HTTP.
- **TLA+**: verification, not testing. That is the connotation the project avoids.

## 3. Proposed examples

Each sample earns its place by exercising something the others don't. `samples/orders` now carries examples of every
invariant scope above, in its overlay.

| Sample | What it exercises |
|:--|:--|
| **Orders** (in the corpus) | idempotency, concurrency, paging, a 202, deprecation; now also a total that must add up, a lifecycle (`pending → paid → shipped`, `cancelled` only from `pending`), and paging and filtering relations |
| **Bookings** | date ranges (`startsAt < endsAt`); `OnlyOne(roomId, deskId)` (IDL); a 409 for an overlap; time zones; reading back with `If-None-Match` |
| **Ledger** | conservation: a transfer leaves the total balance unchanged, and a balance never goes negative; money as decimal strings; an Idempotency-Key where a retry must not pay twice; settlement as a 202 |
| **Catalogue search** | OpenAPI 3.2's `query` operations (the QUERY method, RFC 10008); filters, sorting and paging as metamorphic relations (results are a subset, ordered, and a prefix); facet counts that agree with the results |
| **Feature flags** | JSON Merge Patch and JSON Patch; a version that only goes up; `ETag` on every read; `readOnly` and `writeOnly` |
| **Notifications** | webhooks and callbacks (subscribe, then receive); a signature header; an SSE stream with OpenAPI 3.2's `itemSchema` |
| **Train Travel** (Bump.sh; licence to confirm) | a third-party spec Matchmark can't read today: the robustness target from LAYERS.md |
| **GitHub, Stripe** (fetched, not committed) | scale: thousands of operations, measured in the ledger, not hand-checked |

## Order of work

1. ✓ **The invariants HTTP already promises, in the engine.** Safe, idempotent, `Location` and `readOnly`/`writeOnly`
   (`SafeChangesNothing`, `IdempotentTwice`, `LocationReadable`, `ReadOnlyIgnored`, `WriteOnlyHidden`). They need no
   new grammar, they work against any API, and each is proven by an injected fault.
2. **The audit.** Its numbers come from the suite, so it lands once the suite does what the audit promises.
3. ✓ **Arazzo criteria as postconditions** (`x-matchmark-ensures`), reusing the engine's runtime expressions: each
   success of the operation is checked (`Postcondition`).
4. ✓ **IDL `x-dependencies`,** in the engine: every other case is kept to the dependencies (a valid case repaired or
   dropped), and each dependency gets one case breaking it alone, which must be rejected naming one of its parameters.
   `Idl` reads the IDL grammar (`IF`/`THEN`, `AND`, `OR`, `NOT`, relations, `Or`, `OnlyOne`, `AllOrNone`, `ZeroOrOne`);
   one it can't read is a gap. Generated validation is next.
5. ✓ **CEL rules and transitions,** in the engine. `Cel` is a small evaluator of the subset Kubernetes rules use (see
   its summary); every instance of a schema with `x-matchmark-rules` in a response is checked (`SchemaRule`), and
   `x-matchmark-transitions` between successive readings of a resource in a link workflow (`TransitionRule`).
   Generated validation is next.
6. ✓ **Metamorphic relations,** the prefix relation: declared (`x-matchmark-relations: [{prefix: limit}]`) or inferred
   for a paged list's limit, n results must be the start of n + 1 (`RelationHolds`). `sort` and others are next.
