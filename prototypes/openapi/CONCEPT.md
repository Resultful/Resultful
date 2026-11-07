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

### Generated or owned: two ways to have the server

The production workflow's server side comes in two forms, from the same spec:

| | Generated (`ResultfulParts="Server"`) | Scaffolded (`Resultful.OpenApi.Scaffold`) |
|:--|:--|:--|
| What you get | Minimal API endpoints, rebuilt on every compile | `[ApiController]` controllers (or Minimal API endpoints) as ordinary source, written once |
| Who owns it | the generator; never edited | the team; edited like any other code |
| When the spec changes | updates itself | new files are added, existing files are kept; the compiler shows which handlers no longer fit the contract |
| Handlers | written from scratch | start as a reference implementation that answers with the spec's examples |

**Either way, the contract stays generated** (types, unions, `IPetsHandlers`, validation, client), and the same checks
apply: the round-trip test and the contract suite don't care which form the server takes.

**When to choose which:**
- Scaffold when a team wants the familiar ASP.NET shape, wants to customise the HTTP layer (filters, attributes,
  versioning), or wants to stop depending on the generator for the server.
- Keep it generated when the HTTP layer should never drift from the spec by hand.

The scaffold is also the way off the generator. Run it once, delete the `Server` part, and the service is plain ASP.NET
that still implements a generated contract.

```bash
dotnet run --project Resultful.OpenApi.Scaffold -- Resultful.OpenApi.Sample.Contract/petstore.yaml \
  Resultful.OpenApi.Sample.Scaffolded --api PetStore --contract-namespace PetStore.Api --namespace PetStore.Scaffolded
```

What it writes, for the Pet Store:
- `Controllers/PetsController.cs`:
  - `[HttpGet("/pets/{petId}", Name = "getPet")]` and `[Tags("pets")]`
  - `[ProducesResponseType<Pet>(200, "application/json")]`, one per documented response
  - `[Consumes("application/json")]`
  - an action that validates the request, calls `IPetsHandlers` and turns the outcome into the documented response
- `PetsHandlers.cs`: the reference implementation. Each method returns the spec's example (or a valid sample) as C#
  object construction, not a JSON string, with a TODO to replace it:

  ```cs
  public Task<Result<Pet, GetPetError>> GetPet(long petId, CancellationToken cancellationToken)
  {
      // TODO: replace this reference answer (the spec's 200 Ok) with the real behaviour.
      Pet answer = new Pet { Id = 1, Name = "Rex", Tag = "good-dog", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } };
      return Task.FromResult<Result<Pet, GetPetError>>(global::Resultful.Result.Ok<Pet, GetPetError>(answer));
  }
  ```

- `PetStoreConventions.cs`:
  - JSON as the spec reads it, for MVC and the OpenAPI document
  - request validation from the spec's constraints, in place of MVC's automatic 400
  - MVC accepting `application/json` only, as documented
- `PetStoreOpenApi.cs` and `Program.cs`.

The untouched output is committed as `Resultful.OpenApi.Sample.Scaffolded`, and tested:
- **Faithful.** `ScaffoldTests` checks the scaffolder still writes exactly those files, byte for byte.
- **Conforms before anyone edits it.** `ScaffoldedServerTests` checks the scaffolded controllers' OpenAPI document
  against the spec with no excused differences, sends every operation's reference answer through the
  conformance-checking client, and checks request validation.
- **Minimal API style builds and verifies.** A slow test scaffolds that style into a fresh project and runs
  `dotnet build` with `Verify` on and warnings as errors, so its Map calls, responses and models must match the spec.

#### Verified at compile time

Owned code can drift from the spec, so the generator checks it. Add the spec with `ResultfulParts="Verify"` to a
project with hand-written endpoints, and the build compares every controller action and Minimal API `Map` call against
the spec's operations. Nothing is generated; it only reports:

| Diagnostic | Meaning |
|:--|:--|
| `RAPI010` | an operation in the spec has no endpoint |
| `RAPI011` | an endpoint is not in the spec and not left out of the OpenAPI description |
| `RAPI012` | the spec documents a response the endpoint doesn't declare |
| `RAPI013` | the endpoint declares a response the spec doesn't document |
| `RAPI014` | the endpoint's name isn't the spec's `operationId` |
| `RAPI015` | a route parameter is named differently from the spec |

What it understands:
- **Routes:** controller `[Route]` prefixes, `[controller]` and `[action]` tokens, route constraints, and literal
  `MapGroup` prefixes.
- **Exclusions:** an endpoint marked `[ApiExplorerSettings(IgnoreApi = true)]`, `[NonAction]` or
  `.ExcludeFromDescription()` (on the call or its group) is skipped on purpose. The rule is that an endpoint is either
  in the spec or out of the OpenAPI description.

They are ordinary warnings: with warnings as errors, a gap fails the build, and `.editorconfig` can set each one's
severity.

Responses are read the way ASP.NET Core's API explorer reads them, so what the verifier sees is what the OpenAPI
document will say:
- **Controllers:** `[ProducesResponseType]`, with or without a type. A success declared without a type takes the
  action's return type (`ActionResult<T>`, `T`), and an `object` or `IActionResult` return type is unknown.
- **Minimal API:** the metadata of the handler's typed results (each case of `Results<Ok<Pet>, NotFound>`,
  `ValidationProblem` as a 400 of `HttpValidationProblemDetails`), then `.Produces<T>()`, `.ProducesProblem()` and
  `.ProducesValidationProblem()` on the call or its group. With none of these, a handler returning `T` is a 200 of `T`,
  and any other is a plain 200. `ProblemHttpResult` declares nothing, so problems need `.Produces` or `.ProducesProblem`.

It also checks that every parameter is wired up, using ASP.NET Core's own binding rules. An explicit `[From*]`
attribute wins. Otherwise:
- a name in the route template binds from the route
- a simple type binds from the query
- an interface comes from services
- a complex type binds from the body (for MVC, only with `[ApiController]`)
- framework types such as `CancellationToken` are skipped

| Diagnostic | Meaning |
|:--|:--|
| `RAPI016` | a spec parameter (path, query or header) isn't bound; the message gives the parameter to add |
| `RAPI017` | the endpoint binds a route, query or header value the spec doesn't document |
| `RAPI018` | a parameter is bound with a different type from the spec (`int` for an `int64` id) |
| `RAPI019` | an optional parameter is bound as a non-nullable value with no default. A Minimal API then answers 400 when it's left out, and MVC silently binds `0` |
| `RAPI020` | the spec's body isn't bound, or one is bound that the spec doesn't have. It also catches the classic MVC mistake: without `[ApiController]`, a complex parameter binds from the form and query, not the body |

Wiring the scaffolded controller wrong four ways gives:

```
PetsController.cs(23,6): error RAPI019: PetsController.ListPets (GET /pets): 'limit' is optional in the spec, but binding it as a non-nullable int with no default makes it required; make it int? or give it a default
PetsController.cs(51,6): error RAPI018: PetsController.GetPet (GET /pets/{petId}): 'petId' is bound as int; the spec says long
PetsController.cs(32,6): error RAPI020: PetsController.CreatePet (POST /pets): the spec has a NewPet request body, but the endpoint binds none
PetsController.cs(32,6): error RAPI017: PetsController.CreatePet (POST /pets): 'body' binds query value 'body', which the spec doesn't document
PetsController.cs(83,6): error RAPI017: PetsController.DeletePet (DELETE /pets/{petId}): 'hard' binds query value 'hard', which the spec doesn't document
```

And it checks that the models match the spec. The endpoint side reads the body parameter's type and each declared
response type (`[ProducesResponseType<T>]`, `(typeof(T), status)`, or `ActionResult<T>` for a success declared without
a type). Types are compared by full identity, not by simple name:
- **The generated type is consistent by construction.** `Pet` from the Contract part is the spec's `Pet`, so there is
  nothing to compare. So is a framework type the spec maps to, or one derived from it (`ValidationProblemDetails` for
  `HttpValidationProblemDetails`). This is the design intent: models come from the spec, so they can't disagree with it.
- **Any other type is compared with the schema,** as System.Text.Json (web defaults) would read and write it:
  - property names: camelCase or `[JsonPropertyName]`, skipping `[JsonIgnore]`
  - JSON type and format (`int` is `integer/int32`, which isn't the spec's `int64`)
  - nullability against `required` and `null` in the type
  - missing and extra properties
  - string enums (values from `[JsonStringEnumMemberName]`; an enum without a string converter is written as a number)
  - discriminated unions (`[JsonPolymorphic]` property, `[JsonDerivedType]` values, then each member)
  - arrays, and everything reachable, recursively

| Diagnostic | Meaning |
|:--|:--|
| `RAPI021` (info) | a hand-written type matches the schema today. It is consistent, but not by construction, so the next spec change can silently break it |
| `RAPI022` | a type differs from its schema; one diagnostic per difference, at its JSON path |

Returning a hand-written `PetDto`, `NewPet` where the spec says `Pet`, a drifted `List<DriftedPet>`, and a body on a
204 gives (abridged):

```
PetsController.cs(32,6): info RAPI021: PetsController.CreatePet (POST /pets): the 201 response is PetDto, which matches the schema Pet today; use the generated types so it stays consistent by construction
PetsController.cs(51,6): error RAPI022: PetsController.GetPet (GET /pets/{petId}): the 200 response is NewPet, which differs from the schema Pet: id is missing
PetsController.cs(51,6): error RAPI022: PetsController.GetPet (GET /pets/{petId}): the 200 response is NewPet, which differs from the schema Pet: status is missing
PetsController.cs(23,6): error RAPI022: PetsController.ListPets (GET /pets): the 200 response is List<DriftedPet>, which differs from the schema Pet[]: [].id is int (integer/int32); the schema says integer/int64
PetsController.cs(23,6): error RAPI022: PetsController.ListPets (GET /pets): the 200 response is List<DriftedPet>, which differs from the schema Pet[]: [].status is an enum written as a number; the schema has string values (add [JsonConverter(typeof(JsonStringEnumConverter<PlainStatus>))])
PetsController.cs(23,6): error RAPI022: PetsController.ListPets (GET /pets): the 200 response is List<DriftedPet>, which differs from the schema Pet[]: [].details is discriminated by 'type'; the schema uses 'kind'
PetsController.cs(23,6): error RAPI022: PetsController.ListPets (GET /pets): the 200 response is List<DriftedPet>, which differs from the schema Pet[]: [].details has no derived type for 'cat' (Cat)
PetsController.cs(23,6): error RAPI022: PetsController.ListPets (GET /pets): the 200 response is List<DriftedPet>, which differs from the schema Pet[]: [].owner (Owner) is not in the schema
PetsController.cs(83,6): error RAPI022: PetsController.DeletePet (DELETE /pets/{petId}): the 204 response is Pet, but the spec's 204 response has no body
```

Minimal API bodies and responses are compared the same way.

Constraints are compared too, again as the served document would carry them. ASP.NET Core writes DataAnnotations into
its OpenAPI document, so a hand-written model's `[MinLength]`, `[MaxLength]`, `[Length]`, `[StringLength]`, `[Range]`
and `[RegularExpression]` are read as `minLength`, `maxLength`, `minItems`, `maxItems`, `minimum`, `maximum`,
`exclusiveMinimum`, `exclusiveMaximum` and `pattern`, and each must match the schema. That mapping was checked against
ASP.NET Core 10's own output, which also showed a trap: on a positional record, `[MaxLength(50)] string Name` applies to
the constructor parameter, not the property, so the document doesn't carry it. The verifier says so and gives the fix,
`[property: MaxLength(50)]`. The generated models carry their annotations, so they are consistent by construction here too.

| Diagnostic | Meaning |
|:--|:--|
| `RAPI023` | a bound parameter's constraints differ from its schema's (`[Range(0, 1000)] int? limit` where the spec says `minimum: 1, maximum: 100`) |


The scaffolded Pet Store opts in, so its clean build is itself the proof that every operation is covered. Breaking its
controller five ways at once gives:

```
petstore.yaml(1,1): error RAPI010: The spec's operation 'deletePet' (DELETE /pets/{petId}) has no endpoint: add a controller action or Map call for it
PetsController.cs(82,6): error RAPI011: PetsController.Health (GET /health) is not in the spec: document it, or leave it out of the OpenAPI description (...)
PetsController.cs(51,6): error RAPI012: PetsController.GetPet (GET /pets/{petId}): the spec documents a 404 response, but the endpoint declares none; declare it with [ProducesResponseType]
PetsController.cs(51,6): error RAPI013: PetsController.GetPet (GET /pets/{petId}): the endpoint declares a 410 response, which the spec does not document
PetsController.cs(51,6): error RAPI014: PetsController.GetPet (GET /pets/{petId}) is named 'fetchPet'; name it 'getPet', the spec's operationId, which the OpenAPI description uses
PetsController.cs(63,6): error RAPI015: PetsController.UpsertPet (PUT /pets/{petId}): the route parameter is '{id}', the spec calls it '{petId}'
```

This is the round-trip test's guarantee moved from test time to compile time, for owned code. The round-trip test still
runs, and still catches what attributes and types can't show, such as content types.

The round-trip test earned its keep again here. MVC's JSON input also accepts `application/*+json`, so the controllers
advertised a request content type the spec doesn't document. The scaffolded conventions now remove it, so the server
accepts exactly what the spec documents. The test that builds the Minimal API style also found generated code relying
on a project's implicit usings; generated and scaffolded files now declare their own.

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

#### The generated regression suite: the spec tests itself

The hand-written suites test what someone thought to test. The **Regression** part makes the spec write its own suite:
`ResultfulParts="Regression"` in a test project generates `PetStoreRegression`, which holds the spec as OpenAPI 3.1 JSON
and the suite built from it by `Resultful.OpenApi.Testing`. That runtime needs only System.Text.Json and an
`HttpClient`, so the suite runs against any server, in any language. The suite is rebuilt with the spec, so it can't
drift from it.

**What it generates,** for every operation (the Pet Store gets 90 cases and a workflow):

| Kind | Cases | Expectation |
|:--|:--|:--|
| Example | the spec's request and parameter examples | accepted |
| Valid | only what's required; every value; every member of every `oneOf` (`(cat)`); the same request again; a resource that doesn't exist (the largest id allowed) | accepted, and answered as documented |
| Boundary | each value at each bound: `minLength`, `maxLength`, `minimum`, `maximum`, exclusive bounds, `minItems`, `maxItems`, `null` where allowed | accepted |
| Invalid | one broken constraint each: a required value missing, `null`, the wrong type, too short or long, off its pattern, off its enum, out of range, too big for `int32` or `int64`, an unknown or missing discriminator, a body that isn't JSON, is missing, or is `text/plain` | rejected with a 4xx that names the broken value |
| Workflow | from each response with OpenAPI `links`: follow every link, read back what was written, write it again and read it back, delete it, and read it again | the resource is there, reads back as written, and is gone after the delete |

Every value is checked against the schema before it is sent: a valid case passes every schema, and an invalid one
fails exactly where it says. Free text gets a unique suffix, so repeated creates don't collide, except in a boundary
case, which keeps the exact value it tests. Generation is deterministic, so a failure is reproducible, and each
failure comes with the request as a `curl` command.

**What it checks,** on every response:

| Check | Fails when |
|:--|:--|
| `NotAServerError` | a 5xx the spec doesn't document |
| `StatusDocumented` | a status the operation doesn't document. Rejecting an invalid request with an undocumented 4xx is a warning ("document how it rejects bad input") |
| `ContentTypeDocumented` | a content type the spec doesn't document for that status, or a body where it documents none |
| `BodyConforms` | a body that breaks its schema, reported at the path |
| `HeadersDocumented` | a required response header is missing |
| `ValidAccepted` | a valid request is rejected (400, 415, 422) |
| `InvalidRejected` | an invalid request is accepted |
| `RejectionNamesTheValue` | a validation problem (a body with `errors`) doesn't name the broken value, or a 400 documented as one has no `errors` |
| `ResourceAvailable` | following a link from a success fails |
| `ReadAfterWrite` | reading back what was just written differs from what the write returned |
| `DeletedIsGone` | after a successful delete, reading it back isn't 404 or 410 |

The report also measures **outcome coverage:** which documented operation-and-status pairs any request reached. Against
the Pet Store service it reaches all 12, conflicts and missing pets included.

**Every check is checked.** `SpecRegressionTests` puts a fault injector in front of the real service, one fault per
check: a 404 turned into a 500, or into a 410; JSON sent as `text/plain`; a pet without its status; a valid pet
rejected; an invalid one accepted; errors that name the wrong thing; a pet lost as soon as it's created, or read back
renamed; a delete that doesn't happen. Each fault fails the check it guards. `EngineTests` covers the required header,
which the Pet Store's spec doesn't declare.

**Where it runs:**
- **Against the service, the untouched scaffold and the fake server.** All three pass. Stateless targets (the
  reference implementation and the fake) skip the workflows.
- **As a differential test.** `report.Compare(other)` lists every case and workflow step where two runs answered
  differently: status, then body, ignoring the trace id. The generated Minimal API endpoints and the scaffolded
  `[ApiController]` controllers, both over the real handlers and an empty database, answer every request identically.
  So the two directions from one spec behave alike at the edges, not just in the description.
- **From the command line, against anything:** `scaffold regress petstore.yaml http://localhost:5080` prints the report
  and exits 0 or 1. `RegressCommandTests` runs it against a small hand-written Todo API with a bug planted (it accepts a
  21-character title). The run fails, naming exactly that case. Fixed, it passes.

**What it found** in code that every other check had passed:
1. The generated server answered a body missing its `oneOf` discriminator with a **500**. System.Text.Json throws
   `NotSupportedException` there, not `JsonException`, so ASP.NET Core doesn't treat it as a bad request.
2. Every request the generated server couldn't bind got a 400 that named nothing: a wrong JSON type, a missing required
   property, an unknown discriminator, a missing body, a malformed path or query value. In Production, ASP.NET Core
   writes that 400 with **no body at all**, though the spec documents a validation problem. The generated endpoints now
   answer each of them as the spec's validation problem, naming the value (`name`, `details.goodBoy`, `petId`, `body`).
3. The sample service let one request break creation for everyone. `upsertPet` accepts any id, and once a pet existed
   at the largest `int64`, SQLite's `AUTOINCREMENT` made **every later `createPet` fail with a 500**. The table now uses
   a plain rowid.
4. The scaffolded controllers turned off MVC's invalid-model-state filter, to keep MVC's DataAnnotations out of the way
   of the generated validator. That also turned off binding errors. A malformed body reached the action as `null` and
   threw (**a 500**). Worse, `PUT /pets/abc` bound `petId` to 0, and over the real handlers **created a pet with id 0**.
   The conventions now keep the filter, clear only the validators, and answer binding failures through the same
   generated `PetStoreValidation.BindingError` as the endpoints.
5. The differential run then found the last difference between the two directions: the controllers' problems had no
   `type`. They are now completed by MVC's `ProblemDetailsFactory`, and the answers are identical.

**How to adopt it,** smallest step first:
1. Run `scaffold regress` against an existing API, or generate the contract into its test project and run its tests
   through the client. The service is untouched.
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
| Coverage verification | hand-written endpoints missing an operation, adding one, declaring different responses or names, or wiring parameters and bodies differently from the spec; at compile time | `CoverageTests`, and the scaffolded Pet Store's own build |
| Scaffold fidelity | scaffolded controllers describing or answering differently from the spec | `ScaffoldedServerTests`, `ScaffoldTests` |
| Fluent fake assertions | a consumer making calls it shouldn't, too often, or in the wrong order | `FluentFakeServerTests` |
| Scenario expectations | a consumer calling the API in the wrong order, or with the wrong request | `ScenarioTests`, in-process and over HTTP |
| Fake conformance | a fake answering with something the real API never could | `FakeTests.Every_documented_outcome_of_the_fake_server_conforms_to_the_spec` |
| Black-box regression suite | anything visible over HTTP, written without the generated types | `RegressionSuite` |
| Generated regression suite | a response, status, content type, body or header the spec doesn't allow; a valid request rejected or an invalid one accepted; a rejection that doesn't say what was wrong; a resource that can't be read back as written, or survives its delete | `SpecRegressionTests`, one injected fault per check; `RegressCommandTests` against a hand-written API |
| Differential run | two implementations of the spec answering differently | generated endpoints against scaffolded controllers, over the same handlers |

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
| Next | **Runtime conformance middleware.** The regression engine's response checks, run on the server's own responses. *Observe* mode logs undocumented responses in production, *enforce* mode fails tests. | **Arazzo workflows.** Call sequences beyond what `links` can say, run as stateful tests from the seed scenarios. |
| Then | **Spec diff in CI.** Classify spec changes as additive or breaking, and fail a breaking change that isn't versioned. | **Random inputs.** Schema-derived generators (CsCheck) on top of the deterministic cases, shrinking each failure to its smallest request. |
| Later | **More inputs and hosts.** AsyncAPI for events, other hosts such as Functions or Lambda, through one small internal model of operations and outcomes. | **Parameter constraints in the generated server.** It validates bodies; the suite will catch the gap as soon as a spec constrains a parameter. |

One step serves both modes and comes first: **packaging.** That means a NuGet package with a `.props` file declaring
the build metadata, problem types the contract owns, and a lenient client mode. The prototype can't be adopted without
it.
