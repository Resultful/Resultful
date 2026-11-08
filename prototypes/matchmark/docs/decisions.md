# Decisions

Each decision about the product, as made in the vision review of 8 November 2026 (a question-by-question interview,
numbered Q1 to Q43 as asked). [VISION.md](../VISION.md) is written from these. A new decision is added here with its
date before the vision or the code changes.

## Product

| # | Decision | Why |
|:--|:--|:--|
| Q1 | The first user is someone who already has a spec. Matchmark builds outwards from it: verify, then test against a hosted copy, then flip so the spec leads. | It asks nothing of the service, works for any stack, and each step is useful alone. |
| Q2 | Matchmark becomes its own product, with its own repository. | Teams that want only the test tool or the hosted copies shouldn't take on Resultful. |
| Q3 | Matchmark is primarily .NET. Other languages may come later as more outputs of the same model. | One language keeps one implementation; HTTP already serves every other stack. |
| Q11 | The code moves to its new repository as it is, with its history, before the model refactor. | The move is mechanical and every test proves it; the refactor belongs where it will live. |
| Q12 | Generated contracts use plain unions by default; Resultful and OneOf become an opt-in adapter. | Removes a dependency most consumers don't need. |
| Q18 | The name stays Matchmark. | Match marks are scored across parts so they go back together exactly; that is verify, test and flip. |

## Verify

| # | Decision | Why |
|:--|:--|:--|
| Q7 | Verifying a spec means the spec on its own first (`audit`), then a running API against it (`regress`) whenever there is one. | The first needs only the file; the second finds the defects users care about. |
| Q17 | The tool ships as a dotnet tool and as a container image. | A team without the .NET SDK can still verify its spec. |

## Hosted sessions

These replace the earlier choices of generated TypeScript Mock Service Worker handlers (Q9's TypeScript part, Q13, and
Q26's MSW part).

| # | Decision | Why |
|:--|:--|:--|
| Q9 | Fakes enforce the contract: requests checked, only documented outcomes, state kept, expected calls asserted. | That is what Orval's MSW output lacks and Matchmark already does. |
| Q34 | A C# server hosts a copy of the API per session; any test in any language reaches it over HTTP. The server's implementation is split only where swapping its parts is a low-cost abstraction. | One implementation, in one language, for every stack. |
| Q35 | Sessions are routed by host (`test1.localhost:8080`). | Paths stay exactly as the spec writes them; nothing in the spec is manipulated. |
| Q36 | The consuming application chooses the session id and starts the session; starting it stands in for logging in. | Readable ids, explicit setup, and no silent empty copies. |
| Q41 | The host picks the copy; the session's token, issued in the spec's own security scheme, must belong to that session or the request gets 401. | Requests before login still route; the app's real auth path is exercised; cross-talk between parallel tests is caught. |
| Q37 | Each copy is a twin seeded per session, with scripted outcomes (`Given`) and expected calls (`Should`) on top. | Realistic state for the main path, forced errors where a test wants them. |
| Q38 | .NET tests get a test-framework helper; every other stack uses the HTTP control API. | Nothing language-specific has to be generated or published. |
| Q39 | WebSocket and broker channels are separate per session, and messages are checked against AsyncAPI. | One model for HTTP and events, and no cross-talk. |
| Q40 | Server engineers publish pathways as Arazzo workflows; sessions count which workflows, steps and outcomes were exercised. | A standard format other tools read, and the contract between teams. |
| Q42 | The end-of-run report has two halves, consumer and provider; a run is complete only when both pass. | Both sides are tested against the same pathways. |
| Q43 | The real service, twin and scripted fake meet only at the generated handler interface, and the host picks one per session. | The interface exists and already costs nothing to swap. |
| Q26 | Broker channels are faked by a .NET in-process fake behind the same generated interfaces. (WebSocket channels: see Q39.) | Swapping the real broker for the fake changes nothing else. |

## Flip

| # | Decision | Why |
|:--|:--|:--|
| Q8 | "Reverse engineering" is the migration path: take or derive the spec, verify the application against it, then scaffold the application from it. New applications start at the last step. | Matchmark has most of it already. |
| Q14 | Where an application has no spec, Matchmark derives one from ASP.NET Core's own OpenAPI document first; recorded traffic later. | The document is free and exact about routes and types. |
| Q15 | The whole spine is tried end to end on one real service with a frontend, named later. | It exercises all three steps. |
| Q21 | AsyncAPI follows the same verify and flip steps, with one broker adapter first, chosen by the trial service. | The same promise as for HTTP. |

## Specifications and versions

| # | Decision | Why |
|:--|:--|:--|
| Q19 | Full support of OpenAPI, AsyncAPI, Arazzo and Overlay. GraphQL, gRPC and Protocol Buffers stay out of scope until needed. | One family of ideas fits one model. |
| Q20 | "Full support" means every construct in a supported version is read, anything not yet generated is reported by name, and support is measured against each specification's official examples. | "Full" needs a test that could fail. |
| Q22 | AsyncAPI 2.x is read by forward-porting it to 3.x. | One internal shape; 2.x is still common. |
| Q23 | Forward porting is a command: `matchmark upgrade` writes the migrated spec and lists what changed and what was lost. | A team gets a migration it can commit. |
| Q24 | The model holds the latest version of each format. Older versions are read by forward porting; Matchmark writes whichever version a consumer needs; a version stays supported while a supported .NET release emits it. | One model, and support dates you can look up. |
| Q25 | An older version is viable when a maintained converter or migration guide covers it and nothing the generated code or tests rely on is lost. | A rule, not a judgement. |
| Q29 | The support matrix is generated from the tests. | It can't go stale, and it measures "full". |

## Libraries

| # | Decision | Why |
|:--|:--|:--|
| Q28 | Standard libraries where applicable; where one is insufficient, fork it with Oxbow (upstream plus recorded changes). | Less to own; gaps fixed where everyone benefits. |
| Q30 | AsyncAPI: the official `asyncapi/net-sdk`, forked for 3.1. | The AsyncAPI Initiative's own SDK, Apache 2.0, reads v2 and v3. |
| Q31 | Arazzo and Overlay: BinkyLabs, pinned to exact versions, forked for Arazzo 1.1 and Overlay 1.2. If a BinkyLabs library proves insufficient, Matchmark gets a reader of its own for that format. | MIT, built on Microsoft.OpenApi; the only maintained .NET implementations. |
| Q32 | Matchmark's own readers are retired only when their replacement passes the same tests. | The switch can't silently lose features. |
| Q33 | Each fork change is written so upstream could take it, proposed by a person, and dropped once upstream releases it. | A short stack is cheap to carry. |

## Agents and twins

| # | Decision | Why |
|:--|:--|:--|
| Q4 | Agent-written behaviour is confined to twins, and kept only when the fidelity gate says it helped. | The gate is what makes it safe; a service has no real API to measure against. |
| Q5 | The agent loop is vendor-neutral: Matchmark owns the tasks, the held-out split and the gate; any writer fills it. | It describes what was built and needs no credential Matchmark can't hold. |
| Q16 | Twins and the agent loop are later layers, after the spine. | They are built; ordering them later keeps the near-term story simple. |

## The model and the order

| # | Decision | Why |
|:--|:--|:--|
| Q10 | One model behind every output, built before any new output. | Without it each output repeats the generator's decisions and drifts. |
| Q27 | The roadmap is ordered: own home, the model, verify (alongside the model), hosted sessions, flip, later layers. | It follows verify, test, flip, and puts the model first. |
