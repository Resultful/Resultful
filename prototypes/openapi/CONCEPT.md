# The concept: the spec as an enforceable contract

How the prototype works today. [VISION.md](VISION.md) sets out where it is going; [PETSTORE.md](PETSTORE.md) shows it on
the sample.

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
        PetsHandlers (in-process)  ·  PetStoreClient (any server, over HTTP)  ·  PetStoreFake (generated)
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

## Three ways to use it

| | Production workflow | Testing framework | Fake server |
|:--|:--|:--|:--|
| For | a service designed spec-first | any API: code-first .NET, a legacy service, another language | consumers of an API, before or alongside the real one |
| Generated | Contract and Server parts | the Contract part only, in the test project | Contract and Server parts, over the generated fake |
| You write | the handlers, the contract suite | the contract suite | a ten-line `Program.cs`, or nothing when used in-process |
| The service changes? | it is built on the generated contract | no | no, and it needn't exist yet |
| Checks | compiler, request validation, round-trip document, contract suite on both targets, outcome coverage | response conformance on every call, outcome coverage, round-trip document if the service publishes one | every documented outcome the fake can produce, checked against the spec; its round-trip document |

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

### C. Fake server: cheap to build, cheap to consume

`PetStoreFake` is generated with the contract and implements `IPetsHandlers`, so it serves two purposes:

- **In-process,** it's a test double for any code that depends on the contract. Each operation answers with its first
  documented success until scripted, and every call is recorded:

  ```cs
  var pets = new PetStoreFake()
      .OnGetPet(id => GetPetError.NotFound(new ProblemDetails { Title = $"No pet {id}" }));
  ```

- **Behind the generated endpoints,** it's a fake server: `Resultful.OpenApi.Sample.Fake` is a `Program.cs` of about
  ten lines with no database and no handlers.
  - **Run it:** `dotnet run --project Resultful.OpenApi.Sample.Fake`, on http://localhost:5080.
  - **Script it from any language:**
    - `PUT /__fake/{operationId}` with `{"status": 404, "body": {...}}` sets the next answers
    - `GET /__fake/calls` lists the calls received
    - `DELETE /__fake` resets everything

    These control endpoints are left out of the OpenAPI document.

Default answers come from the spec:
- the response's `example`, or the schema's
- otherwise a sample built to pass the schema: required properties, lengths and ranges, the first enum value, and the
  first `oneOf` member with its discriminator

If a required string has a pattern the generated sample doesn't match, the generator asks for an example (`RAPI005`)
rather than serve an invalid body.

#### The fluent fake server

For .NET tests, the generator also writes a fluent API over the fake, typed per operation from the spec, and
`PetStoreFakeServer`, which hosts it on a real Kestrel port:

```cs
await using var server = await PetStoreFakeServer.StartAsync();       // http://127.0.0.1:{free port}/

server.Given.GetPet(petId: 7).Returns.Ok(rex).Then.NotFound();        // answers named after the spec's responses
server.Given.CreatePet(where: body => body.Name == "Rex").Returns.Conflict();
server.Given.NothingElse();                                             // anything unmatched fails

// ... the code under test calls server.BaseAddress, or server.CreateClient() ...

server.Should().HaveReceived.GetPet(petId: 7).Twice();
server.Should().HaveReceivedInOrder(c => c.CreatePet(), c => c.GetPet(petId: 7));
server.Should().HaveReceivedNoOtherCalls();
```

- **`Given`** sets up rules. A call matches by argument equality (leave an argument out to match anything) or a typed
  predicate (`where:`). The latest matching rule wins, so a test can narrow an earlier, broader rule. `Returns`
  offers only the operation's documented responses, by name (`Ok`, `Created`, `NotFound`, `Conflict`, `BadRequest`).
  An answer without a body uses the spec's example, and an answer whose body breaks the schema throws when given.
  `Then` adds the next answer in a sequence; the last one repeats.
- **`Should()`** checks what was received, in AwesomeAssertions' style:
  - `HaveReceived.{Operation}(...)`, followed by `Once`, `Twice`, `Times(n)`, `Never`, `AtLeast(n)` or `AtMost(n)`
  - `HaveReceivedInOrder(...)`
  - `HaveReceivedNoOtherCalls()`, which fails if any call wasn't covered by an earlier check
  - `HaveMetItsScenario()`

  Failures throw `FakeAssertionException`, which any test framework reports, with the expectation and the calls
  actually received:

  ```
  Expected the fake to have received getPet(petId: 7) once, but it received it 0 times.
  It received:
    1. getPet(petId: 1)
    2. deletePet(petId: 7)
  ```

The fluent API and scenarios are two styles over the same fake, and they combine: a scenario can run on a fluent
server, and be checked with `HaveMetItsScenario()`. Use fluent rules for a test's own setup, and scenarios for flows
shared as files or across languages.

What it takes from the C# fake-server libraries, and what it adds:

| Library | Taken | Added here |
|:--|:--|:--|
| WireMock.Net | a real server on a port; `Given` then answer; sequences of answers | typed per operation; answers limited to the spec's responses, with valid bodies |
| MockHttp | stub versus expectation; failing on outstanding or unmatched calls | `NothingElse()`, `HaveReceivedNoOtherCalls()`, scenarios |
| Flurl HttpTest | checks after the fact: `ShouldHaveCalled(...).Times(n)` | typed `HaveReceived.GetPet(petId: 7).Twice()`, in-order checks, messages listing the calls received |
| JustEat HttpClient Interception | words that exist to read well; definitions in JSON | `Returns` / `Then`; scenarios as JSON, checked against the spec |

#### Scenarios: expected calls, in order or not

To check how a consumer uses the API, give the fake a scenario: the calls it expects, what each request must contain,
and the documented answer to give. A scenario can be written two ways, both producing the same model (`Scenario` in
`Resultful.OpenApi.Fakes`):

```cs
// In code: a generated, typed builder with one method per operation.
var fake = new PetStoreFake().UseScenario(PetStoreScenario.InOrder()
    .CreatePet(expect: new { body = new { name = "Rex" } }, status: 201)
    .GetPet(expect: new { petId = 7 }, times: 2)
    .DeletePet(expect: new { petId = 7 })
    .Build());
```

```json
// Declaratively: the same steps in JSON (Scenarios/adopt-rex.json).
{ "order": "InOrder", "steps": [
  { "operation": "createPet", "expect": { "body": { "name": "Rex" } }, "status": 201 },
  { "operation": "getPet", "expect": { "petId": 7 }, "status": 200, "times": 2 } ] }
```

How a scenario runs:
- **Expectations are subsets.** A step's `expect` matches a request if every property it gives matches the request's
  arguments, with the body under `body`. A mismatch names the field:
  `body.details.kind expected "cat", got "dog"`.
- **Order.** `InOrder` requires each call to match the next step. `AnyOrder` accepts any step with calls left, and
  `times` counts calls per step. An out-of-order call names both steps: `getPet was called out of order: step 2
  (getPet → 200) comes after step 1 (createPet → 201)`.
- **Failing calls.** A call the scenario doesn't expect throws `FakeExpectationException` in-process; over HTTP it
  becomes a `500` problem titled "Fake expectation failed". Either way it is recorded.
- **Verification.** `Verify()` reports every failure, plus every step not called as many times as it should have been.

A scenario is checked against the spec before it runs. `PetStoreFake.Check` lists everything wrong at once: unknown
operations, undocumented statuses, `expect` keys that aren't parameters, answer bodies that break the schema, and
`times` below 1. A typo in a declarative file fails on load, not halfway through a test.

#### An API that stands up APIs

The fake server is also a hub of isolated fakes, so many tests can share one fake-server process (`/__fake` is left
out of the OpenAPI document):
- `POST /__fake/sessions` with a scenario (or none) stands up a fresh fake API and returns its base URL,
  `/sessions/{id}/`. A scenario that breaks the spec is refused with every problem listed.
- Calls under that base URL go to that session's fake only. The default fake at the root, and other sessions, are
  unaffected.
- `GET /__fake/sessions/{id}` returns the verification report (passed, failures, calls), and
  `DELETE /__fake/sessions/{id}` ends the session.

A consumer in any language can therefore post a scenario, point its client at the base URL, run its test and read
the report. A .NET consumer passes the base URL to `PetStoreClient`, which also checks every answer against the spec.

What makes it trustworthy, not just cheap:
- **Same edges as the real service.** It runs the real service's generated endpoints, so a consumer sees the same
  request validation, the same `problem+json` errors and the same OpenAPI document. `FakeTests` checks the document
  with the round-trip comparer.
- **Only documented outcomes, with valid bodies.** Scripting a status the spec doesn't document is refused,
  in-process (`Respond` returns false) and over HTTP (404 with a reason). So is a scripted body that breaks the schema.
- **Every outcome it can give is checked.** `FakeTests` scripts each of the 12 documented outcomes in turn and reads
  each through the conformance-checking client. Breaking the sample builder on purpose fails it at the exact field.
- **Tests carry over.** A consumer's tests written against `IPetsHandlers` with the fake run unchanged against
  `PetStoreClient` and the real service.

## What each check proves

| Check | Catches | Shown by |
|:--|:--|:--|
| The compiler, on generated types and unions | a handler that leaves a documented outcome unhandled, or returns one that isn't documented | `upsertPet`'s 409 forcing a handler change |
| Request validation (endpoint, and `ValidatingPetsHandlers`) | a request that breaks the schema, rejected identically in-process and over HTTP | `ContractSuite.*_breaking_the_schema_*` on both targets |
| Round-trip document | the server describing a different API from the spec: extra endpoints, a constraint, a schema shape | `SpecDriftTests`; breaking it on purpose is in the README |
| Response conformance (client) | a server sending something the spec doesn't allow | `ClientConformanceTests` |
| Contract suite on two targets | behaviour that differs between the handlers and the running service | `ContractSuite`, 10 tests × 2 targets |
| Outcome coverage | documented behaviour no test reaches | `ContractSuite.Every_documented_outcome_was_reached` |
| Fluent fake assertions | a consumer making calls it shouldn't, too often, or in the wrong order | `FluentFakeServerTests` |
| Scenario expectations | a consumer calling the API in the wrong order, or with the wrong request | `ScenarioTests`, in-process and over HTTP |
| Fake conformance | a fake answering with something the real API never could | `FakeTests.Every_documented_outcome_of_the_fake_server_conforms_to_the_spec` |
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
