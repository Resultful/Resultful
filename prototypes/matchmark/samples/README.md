# Sample corpus

Specs that Matchmark is measured against, beyond the Pet Store. Each is here for a reason, and `CorpusTests` keeps a
ledger of what the generator has to skip in each and what the regression engine builds from it. Supporting more of a
sample changes the ledger; so does breaking something that worked.

| Sample | Why it's here | Layers |
|:--|:--|:--|
| Pet Store (`../Matchmark.Sample.Contract/petstore.yaml`) | The worked example: fully supported, with a service, a scaffold and a fake. | links |
| `museum/` | A real-world OpenAPI 3.1 document written by someone else (Redocly, MIT). Uses what the Pet Store doesn't: primitive component schemas, `allOf`, named arrays. | Arazzo workflows |
| `orders/` | Written to exercise behaviour across calls and over time: an Idempotency-Key, ETag and If-Match, a cursor, a 202 and an operation to poll, rate limits, deprecation, webhooks. | Overlay, Arazzo |
| `catalogue/` | OpenAPI 3.2: a search sent as a QUERY (RFC 10008) and an export streamed as JSON Lines (`itemSchema`), with a declared prefix relation and postconditions in its overlay. Its generated server runs against the suite (`CatalogueTests`). | Overlay |
| `payments/` | Request bodies as forms, the way Stripe takes them (nested values in brackets, numbers and true or false as text), and a `default` error response carrying its status (a declined card is a 402). Its generated server runs against the suite, random requests included (`FormTests`). | |
| `bookings/` | Written for invariants the schema can't state, in its overlay: a booking ends after it starts and is for a room or a desk, not both (CEL); a search's filters depend on each other (IDL); a new booking is booked (a postcondition). Its generated server enforces them (`GeneratedRulesTests`). | Overlay |

Today's ledger:

```
petstore: generator skips 0; suite 90 cases, 1 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps; audit 6 warnings, 14 findings, 79%
museum: generator skips 3; suite 100 cases, 0 workflows from links, 3 of 3 Arazzo workflows runnable, 0 gaps, 28 webhook cases; audit 12 warnings, 20 findings, 82%
orders: generator skips 0; suite 90 cases, 2 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps, 19 webhook cases; audit 3 warnings, 8 findings, 91%
bookings: generator skips 0; suite 81 cases, 1 workflows from links, 0 of 0 Arazzo workflows runnable, 0 gaps; audit 0 warnings, 1 findings, 100%
catalogue: generator skips 0; suite 73 cases, 1 workflows from links, 0 of 0 Arazzo workflows runnable, 0 gaps; audit 1 warnings, 2 findings, 97%
payments: generator skips 0; suite 56 cases, 1 workflows from links, 0 of 0 Arazzo workflows runnable, 0 gaps; audit 0 warnings, 1 findings, 100%
```

The Arazzo files are run by the regression engine: every workflow in the corpus is runnable, including the Museum's
`operationPath` steps and its workflow in a second document. The Overlay files are applied by the generator, the CLI
and the engine. The Orders overlay carries one example of each kind of invariant from
[COMPLETENESS.md](../COMPLETENESS.md), and the Bookings overlay the invariants its generated server enforces.
COMPLETENESS.md proposes the next samples (Ledger, Feature flags, Notifications).
Large public specs (GitHub, Stripe) are fetched, not committed (`samples/large/fetch.sh`), and run for scale by
`LargeSpecTests` when `MATCHMARK_LARGE_SPECS` names where they were fetched to. Their ledger (November 2026):

```
github: 1272 operations, 1209 generated (95%), 0 compile errors
stripe: 612 operations, 578 generated (94.4%), 0 compile errors
```

The first run found that the generator failed outright on GitHub's spec, and that what it wrote didn't compile; both
are fixed (LAYERS.md). Stripe was at 0%: it sends every request body as `application/x-www-form-urlencoded`, and
documents its errors only as a `default` response. Both are generated now (the Payments sample). What's left of Stripe
is its list filters, deep-object query parameters.
