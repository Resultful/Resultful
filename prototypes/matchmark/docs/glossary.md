# Glossary

| Term | Meaning |
|:--|:--|
| **Audit** | `matchmark audit`: what a spec leaves out that the suite, fake and server would use, measured in cases. |
| **Bridge** | An HTTP adapter in front of a message consumer or broker: `POST /{operationId}` delivers a message; `GET /{operationId}` receives one. |
| **Case** | One generated request with its expectation: valid, boundary or invalid. |
| **Check** | A named property every response or run is held to ([checks](reference/checks.md)). |
| **Contract** | The generated types, outcome unions and handler interface (`I{Api}Handlers`). The server, client, twin and fakes all implement it. |
| **Copy** | A session's own instance of the API, behind the generated endpoints: usually a twin with a script on top. |
| **Fidelity** | The share of a suite's steps a fake answers as the real API does. |
| **Flip** | Moving an application to spec-first: derive, verify, scaffold, verify at compile time ([flip](reference/flip.md)). |
| **Forward port** | Converting an older version of a specification to the latest one Matchmark's model holds. |
| **Gate** | `FidelityLoop.Judge`: keeps a change to a twin only when held-out fidelity rose and no operation fell. |
| **Held out** | The share of recorded sequences a twin's writer never sees, on which changes are judged. |
| **Invariant scope** | What an invariant spans: one payload, one request's parameters, a request and its response, two readings of a resource, or (proposed) a whole collection. |
| **The model** | The semantic model every output is generated from (roadmap step 2, [the model](reference/model.md)). |
| **Outcome** | A documented status (or range) of an operation, with its body. Each is a case of the operation's union. |
| **Outcome coverage** | The share of documented outcomes a test suite reached. |
| **Oxbow** | The tool that keeps a fork as upstream plus a short stack of recorded changes ([rtkelly-labs/oxbow](https://github.com/rtkelly-labs/oxbow)). |
| **Owned code** | Code a team keeps and edits: scaffolded handlers, a twin's behaviour. The opposite of generated code. |
| **Part** | A slice of what the generator writes: `Contract`, `Server`, `FakeServer`, `Regression`, `Verify` ([build settings](reference/build.md)). |
| **Pathway** | An Arazzo workflow a provider publishes as a journey its consumers must support. |
| **Recording** | The real API's answers to a suite, saved as JSON and replayed in pull requests. |
| **Report** | A session's end-of-run answer: the consumer's half, the provider's half, and whether the run is complete. |
| **Round trip** | The check that the running service describes itself exactly as the spec does. |
| **Scenario** | Expected calls, in order or not, written in code or JSON. |
| **Session** | A test's own copy of the API, routed by host, started by the consumer with an id it chooses. |
| **Session host** | The C# server that hosts the sessions. |
| **Spine** | The three steps the vision is built on: verify, test against a hosted copy, flip. |
| **Suite** | The regression cases and workflows generated from a spec. |
| **Trait** | A declared behaviour from a fixed vocabulary (unique, references, …), read by one implementation in Matchmark ([invariants](reference/invariants.md)). |
| **Twin** | A stateful fake from the spec alone, measured against the real API ([twins](reference/twins.md)). |
| **Writing down** | Writing a spec in an older version of its format than the model holds, for a consumer that needs it. |
