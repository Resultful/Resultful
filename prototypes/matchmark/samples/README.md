# Sample corpus

Specs that Matchmark is measured against, beyond the Pet Store. Each is here for a reason, and `CorpusTests` keeps a
ledger of what the generator has to skip in each and what the regression engine builds from it. Supporting more of a
sample changes the ledger; so does breaking something that worked.

| Sample | Why it's here | Layers |
|:--|:--|:--|
| Pet Store (`../Matchmark.Sample.Contract/petstore.yaml`) | The worked example: fully supported, with a service, a scaffold and a fake. | links |
| `museum/` | A real-world OpenAPI 3.1 document written by someone else (Redocly, MIT). Uses what the Pet Store doesn't: primitive component schemas, `allOf`, named arrays. | Arazzo workflows |
| `orders/` | Written to exercise behaviour across calls and over time: an Idempotency-Key, ETag and If-Match, a cursor, a 202 and an operation to poll, rate limits, deprecation, webhooks. | Overlay, Arazzo |

Today's ledger:

```
petstore: generator skips 0; suite 90 cases, 1 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps
museum: generator skips 29; suite 100 cases, 0 workflows from links, 3 of 3 Arazzo workflows runnable, 0 gaps
orders: generator skips 3; suite 89 cases, 2 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps
```

The Arazzo files are run by the regression engine: every workflow in the corpus is runnable, including the Museum's
`operationPath` steps and its workflow in a second document. The Overlay files are not read by anything yet. They are the targets [LAYERS.md](../LAYERS.md) plans for;
the Orders overlay also carries one example of each kind of invariant from [COMPLETENESS.md](../COMPLETENESS.md), which
proposes the next samples (Bookings, Ledger, Catalogue search, Feature flags, Notifications).
Large public specs (GitHub, Stripe) belong in a separate, fetched-not-committed run, for scale rather than design.
