# Questions

What is still open. Each question names the assumption work proceeds on until it's answered, so nothing waits on it.
When one is answered, move the answer to [decisions.md](decisions.md) with its date and delete it here.

## Product and repository

**1. What is the new repository called, and where does it live?** The vision says Matchmark gets its own repository
(Q2, Q11). The organisation (`rtkelly-labs`, alongside Lake.NET and Oxbow, or another) and the package id prefix
(`Matchmark.*`) aren't decided.
*Assumption:* `rtkelly-labs/matchmark`, packages `Matchmark.*` as today.

**2. What licence does it carry?** Resultful is MIT; the forks it will depend on are MIT and Apache 2.0.
*Assumption:* MIT.

**3. Which service is the first trial of the whole spine?** Q15 decided on one real ASP.NET Core service with a
frontend, named later.
*Assumption:* none; step 5 waits for a name.

## Hosted sessions

**4. How long does a session live?** Nothing says when an abandoned session is cleaned up.
*Assumption:* a session ends when its consumer ends it, or after 30 minutes without a request, whichever comes first.

**5. What does a session's token look like when the spec declares no security scheme?** Q41 issues the token in the
spec's own scheme. An API with no `securitySchemes` has none to issue in.
*Assumption:* no token is issued or checked; the host alone picks the copy.

**6. Is there a fallback for clients that can't resolve `*.localhost`?** Browsers resolve it to loopback, but some
resolvers (some Linux containers, older HTTP clients) don't. Host routing (Q35) needs the `Host` header, not DNS: a
client can connect to `localhost` and send `Host: test1.localhost`.
*Assumption:* the host also accepts the session id in an `X-Matchmark-Session` header, for clients that can't set a
host, and the path prefix `/sessions/{id}/` the prototype has today stays until host routing is proven.

**7. Where does the provider half of the report come from?** Q42's report pairs the consumer's session with the real
service passing the same pathways. Running the real service on every report request is slow and needs it reachable.
*Assumption:* the provider half is the latest `matchmark regress` result for those pathways, recorded with its date
(as fidelity recordings are), and the report says how old it is.

**8. Does the twin's CLI become the session host?** `matchmark twin` serves one twin today.
*Assumption:* a new `matchmark host` command serves the session host; `matchmark twin` stays as the single-copy
shortcut.

## Specifications

**9. Which official examples measure "full support"?** Q20 measures support against each specification's official
examples. OpenAPI publishes examples in `OAI/learn.openapis.org` and the specification repository; AsyncAPI in
`asyncapi/spec` (`examples/`); Arazzo and Overlay in their specification repositories.
*Assumption:* the examples folders of each specification's own repository, at a pinned commit, fetched (not committed)
as the large specs are.

**10. Does the engine keep its own JSON-level reading once the libraries are adopted?** The engine reads specs as JSON
with System.Text.Json and needs no other dependency today. Adopting Microsoft.OpenApi, the AsyncAPI SDK and BinkyLabs
(Q28 to Q31) is about reading; the engine could keep JSON and receive normalised documents from them.
*Assumption:* libraries read and forward-port; the engine keeps working on the normalised JSON they write, so
`Matchmark.Testing` stays dependency-light until the model (step 2) replaces both.

**11. Should AsyncAPI 2.x be converted by the official JavaScript converter instead of Matchmark's port?** The chosen
AsyncAPI SDK (Q30) reads v2 and v3 but doesn't convert, and the official converter is `@asyncapi/converter`, a
JavaScript package. Q25 allows an official migration guide as the basis, and Matchmark has ported the guide section by
section (`AsyncApiUpgrade`), keeping the tool .NET-only (Q3). The cost is keeping the port in step with the guide.
*Assumption:* keep the C# port; propose it to `asyncapi/net-sdk` as a contribution through its Oxbow fork, so it lives
in the standard library (Q33).
