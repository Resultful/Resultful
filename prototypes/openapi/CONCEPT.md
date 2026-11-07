# The concept: the spec as an enforceable contract

How the prototype works today. [VISION.md](VISION.md) sets out where it is going.

Most spec-first tools generate a skeleton once, and the spec drifts from the code from then on. This prototype keeps
the spec enforceable for the life of the service. The spec fixes every part of the API except the behaviour inside
each operation. Independent checks prove that the code, the running server and the tests all match it.

```
                               petstore.yaml
                                     │  source generator
              ┌──────────────────────┴───────────────────────┐
              ▼                                              ▼
      Contract (any caller)                          Server (the service)
      ─────────────────────                          ────────────────────
      types, outcome unions                          endpoints → IPetsHandlers
      IPetsHandlers  ◄── the seam ──►                OpenAPI self-description
      validation, ValidatingPetsHandlers
      PetStoreClient : IPetsHandlers
              │
              ▼
      implementations of the one interface:
        PetsHandlers (in-process)  ·  PetStoreClient (any server, over HTTP)  ·  fakes, adapters
```

## The contract is an interface

The generator turns each operation into a method whose return type lists everything the spec documents. For example,
`getPet` becomes `Task<Result<Pet, GetPetError>>`, where `GetPetError` holds the 404 case. Everything follows from
treating that interface (`IPetsHandlers`) as the contract, not the HTTP surface:

- **The service implements it.** The generated endpoints call the handlers and turn each outcome into its documented
  response. An undocumented response cannot be returned, and leaving a documented one unhandled doesn't compile.
- **The client implements it too.** `PetStoreClient` calls the API over HTTP and returns the same `Result` and union
  types. A caller handles a 404 as `GetPetError.NotFound`, not as a status code to remember.
- **A decorator applies the boundary's rules.** `ValidatingPetsHandlers` runs the spec's request validation in front of
  any implementation, so an in-process implementation rejects an invalid body exactly as the endpoint does.
- **Tests are written once against the interface,** then run against any implementation.

Because the client implements the same interface the server does, the HTTP layer becomes an implementation detail the
tests can include or leave out.

## Two ways to use it

| | Production workflow | Testing framework |
|:--|:--|:--|
| For | a service designed spec-first | any API: code-first .NET, a legacy service, another language |
| Generated | Contract and Server parts | the Contract part only, in the test project |
| You write | the handlers, the contract suite | the contract suite |
| The service changes? | it is built on the generated contract | no |
| Checks | compiler, request validation, round-trip document, contract suite on both targets, outcome coverage | response conformance on every call, outcome coverage, round-trip document if the service publishes one |

### A. Production workflow: spec-first services

1. **The spec changes first,** in a pull request reviewed like code. Response codes, errors and constraints are API
   decisions made in the spec, not side effects of an implementation.
2. **The build generates both halves.** The Contract part goes into a package that consumers reference. The Server part
   goes into the service: `ResultfulParts="Contract"` or `"Server"` on the spec's `AdditionalFiles` item.
3. **The compiler finds the work.** A new error response is a new case in a union, and every handler and consumer that
   matches on it fails to build until it handles it. The `upsertPet` 409 went in this way.
4. **CI gates,** all implemented here:
   - the build itself: completeness, plus the generator's diagnostics for anything unsupported
   - the round-trip test: ASP.NET Core's own document of the server matches the spec exactly
   - the contract suite, in-process and over HTTP, with the client checking every response
   - outcome coverage: every documented operation and status reached by the suite
5. **Consumers upgrade by package version.** The Contract package version is the spec version, and breaking changes
   show up as compile errors in the consumer, not 500s in production.

Planned for this mode: see *What's next*.

### B. Testing framework: the spec as the oracle

Generate only the Contract part into a test project. The service needn't be .NET, needn't be generated, and needn't
change:

- **Conformance testing.** Every call through `PetStoreClient` is checked:
  - an undocumented status
  - the wrong content type
  - a body that fails to deserialise
  - a body that breaks the schema's constraints

  Each throws `ContractViolationException`. Any test that uses the client is therefore also a conformance test of the
  server. `ClientConformanceTests` shows each check against canned responses. One case is the exact bug this
  prototype's own server had: a 404 problem sent as `application/json`.
- **Outcome coverage.** `PetStoreContract.DocumentedOutcomes` lists every operation-and-status pair; `client.Observed`
  lists the ones a run reached. Code coverage says which lines ran. This says which documented behaviours were tested,
  which is the measure that matters for an API. Removing the one test that reaches `upsertPet 409` fails the suite with
  `11/12 documented outcomes reached … {"upsertPet 409"}`.
- **Migration and differential testing.** Point two clients at the old and new implementations and run the same suite
  against both. Comparing the results, not just checking each one, finds behavioural differences the spec doesn't
  cover.
- **Fakes for consumers.** A consumer's tests can implement `IPetsHandlers` by hand. Because the fake has the same
  outcome types as the real client, it cannot return something the real API never would.

**How to adopt it,** smallest step first:
1. Generate the contract into the test project of an existing API and run its tests through the client. The service is
   untouched.
2. Add the round-trip test if the service already publishes an OpenAPI document.
3. Generate the server part for new endpoints.

## What each check proves

| Check | Catches | Shown by |
|:--|:--|:--|
| The compiler, on generated types and unions | a handler that leaves a documented outcome unhandled, or returns one that isn't documented | `upsertPet`'s 409 forcing a handler change |
| Request validation (endpoint, and `ValidatingPetsHandlers`) | a request that breaks the schema, rejected identically in-process and over HTTP | `ContractSuite.*_breaking_the_schema_*` on both targets |
| Round-trip document | the server describing a different API from the spec: extra endpoints, a constraint, a schema shape | `SpecDriftTests`; breaking it on purpose is in the README |
| Response conformance (client) | a server sending something the spec doesn't allow | `ClientConformanceTests` |
| Contract suite on two targets | behaviour that differs between the handlers and the running service | `ContractSuite`, 10 tests × 2 targets |
| Outcome coverage | documented behaviour no test reaches | `ContractSuite.Every_documented_outcome_was_reached` |
| Black-box regression suite | anything visible over HTTP, written without the generated types | `RegressionSuite` |

The black-box suite and the contract suite overlap on purpose. The contract suite can't catch a mistake the generator
makes consistently on both sides, because the server and the client come from the same generator. The black-box suite
reads raw JSON and status codes, so it can.

## Design decisions

- **Validation failures are an error case.** A documented 400 validation response is a case of the operation's error
  union (`CreatePetBadRequest`), not something only the endpoint can produce. Handlers can return domain validation
  failures in the documented shape, and the client sees them as a typed case.
- **The client is strict.** Any deviation throws, which is right for tests. A production consumer may prefer to log a
  violation and carry on; that becomes an option on the client, not a separate client.
- **The in-process target skips serialisation, and the HTTP target includes it.** Running the suite against both
  separates "the behaviour is wrong" from "the HTTP mapping is wrong".
- **Contract and Server are parts of one generator,** chosen per project. One spec can feed a contract package, a
  service and a test project, and they can't disagree.

## Limits

- The Contract part uses ASP.NET Core's `ProblemDetails` types, so a contract package needs the ASP.NET Core shared
  framework. A client-only package should generate its own problem types.
- The generator's spec coverage is the README's *Not supported yet* list: no `allOf`, no inline objects, no form
  bodies, at most six responses per operation.
- `PetStoreContract` and `ContractViolationException` are generated per API into its namespace. Two specs sharing a
  namespace would collide.

## What's next

In order of value for each mode:

| | Production workflow | Testing framework |
|:--|:--|:--|
| Next | **Runtime conformance middleware.** The client's checks, run on the server's own responses. *Observe* mode logs undocumented responses in production, *enforce* mode fails tests. | **Differential runner.** One suite, two clients (old and new), results compared outcome by outcome. |
| Then | **Spec diff in CI.** Classify spec changes as additive or breaking, and fail a breaking change that isn't versioned. | **Workflows.** Arazzo files describing call sequences, run as stateful tests with generated inputs, from the seed scenarios. |
| Later | **More inputs and hosts.** AsyncAPI for events, other hosts such as Functions or Lambda, through one small internal model of operations and outcomes. | **Generated inputs.** Schema-derived generators (CsCheck), so the suite explores beyond hand-picked examples. |

One step serves both modes and comes first: **packaging.** That means a NuGet package with a `.props` file declaring
the build metadata, problem types the contract owns, and a lenient client mode. The prototype can't be adopted without
it.
