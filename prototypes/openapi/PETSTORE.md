# The Pet Store, end to end

A guided tour of the sample, following one spec from YAML to a running service, a typed client, a fake server and the
tests that hold them together. [VISION.md](VISION.md) says why this exists and [CONCEPT.md](CONCEPT.md) how it works in
general; this document shows it working on one example. Every snippet is from the repository, its generated output, or a recorded
run; long ones are abridged, marked with `...`.

## Run it

```bash
cd prototypes/openapi
dotnet test Resultful.OpenApi.slnx                           # 104 tests: generator and sample
dotnet run --project Resultful.OpenApi.Sample                # the real service (SQLite, Demo seed)
dotnet run --project Resultful.OpenApi.Sample.Fake           # the fake server, http://localhost:5080
```

Generated code is written to `obj/generated/` in the Contract, Sample and Fake projects, so it can be read like any
other source. Nothing generated is checked in.

## The pieces

```
Resultful.OpenApi.Sample.Contract/petstore.yaml        the spec: the one source of truth
          │
          ├─▶ Resultful.OpenApi.Sample.Contract        Contract part: types, unions, IPetsHandlers, validation,
          │                                            PetStoreClient, PetStoreFake, PetStoreScenario
          ├─▶ Resultful.OpenApi.Sample                 Server part: endpoints, OpenAPI configuration
          │                                            + PetsHandlers.cs (hand-written), SQLite through EF Core
          └─▶ Resultful.OpenApi.Sample.Fake            Server and FakeServer parts over the fake: no hand-written behaviour
                                                       (a ten-line Program.cs)

Resultful.OpenApi.Sample.Tests                         everything above, checked against the spec
```

The same spec file feeds three projects. Each takes the parts it needs (`ResultfulParts` on its `AdditionalFiles` item),
so the projects can't disagree about the API.

## 1. The spec

Five operations on pets:

| Operation | Request | Documented responses |
|:--|:--|:--|
| `listPets` | `GET /pets?limit=` | 200 |
| `createPet` | `POST /pets` with a `NewPet` | 201, 400 validation, 409 name taken |
| `getPet` | `GET /pets/{petId}` | 200 (with an example), 404 |
| `upsertPet` | `PUT /pets/{petId}` with a `NewPet` | 200 replaced, 201 created, 400 validation, 409 name taken |
| `deletePet` | `DELETE /pets/{petId}` | 204, 404 |

The schemas cover the cases a generator usually gets wrong:
- `PetStatus`, a string enum
- `PetDetails`, a `oneOf` of `Dog` and `Cat` with a `kind` discriminator
- a nullable `tag` with a `pattern`
- `livesLeft` with a `minimum` and `maximum`
- `ProblemDetails` and `HttpValidationProblemDetails`, mapped onto ASP.NET Core's own types

```yaml
/pets/{petId}:
  get:
    operationId: getPet
    responses:
      '200':
        content:
          application/json:
            schema: { $ref: '#/components/schemas/Pet' }
            example: { id: 1, name: Rex, tag: good-dog, status: available, details: { kind: dog, goodBoy: true } }
      '404':
        content:
          application/problem+json:
            schema: { $ref: '#/components/schemas/ProblemDetails' }
```

## 2. The spec becomes a contract

`Resultful.OpenApi.Sample.Contract` holds nothing but the spec and a project file. The build generates the contract:

**One interface for the whole API.** Every method's return type lists what the spec documents:

```cs
public interface IPetsHandlers
{
    Task<IReadOnlyList<Pet>> ListPets(int? limit, CancellationToken cancellationToken);
    Task<Result<Pet, CreatePetError>> CreatePet(NewPet body, CancellationToken cancellationToken);
    Task<Result<Pet, GetPetError>> GetPet(long petId, CancellationToken cancellationToken);
    Task<Result<UpsertPetSuccess, UpsertPetError>> UpsertPet(long petId, NewPet body, CancellationToken cancellationToken);
    Task<VoidResult<DeletePetError>> DeletePet(long petId, CancellationToken cancellationToken);
}
```

Read the return types as the spec:
- **`listPets`** documents no error, so it has no error channel.
- **`deletePet`** has no body on success, so it returns `VoidResult`.
- **`upsertPet`** has two successes, so it gets a success union, `UpsertPetSuccess` (`Ok` or `Created`).

**A union per operation for its documented errors,** each case carrying its body:

```cs
public sealed record GetPetNotFound(ProblemDetails Body);

public sealed class GetPetError : OneOfBase<GetPetNotFound>
{
    public static GetPetError NotFound(ProblemDetails body) => new GetPetNotFound(body);
}
```

**Records for the schemas.** `PetDetails` is a polymorphic record that System.Text.Json reads and writes by `kind`:

```cs
public sealed record Pet
{
    [JsonPropertyName("name")]
    [MinLength(1)]
    [MaxLength(50)]
    public required string Name { get; init; }
    // id, tag, status, details ...
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Dog), "dog")]
[JsonDerivedType(typeof(Cat), "cat")]
public abstract record PetDetails;
```

**Also generated:**
- `PetStoreValidation`, which checks every schema's constraints
- `ValidatingPetsHandlers`, which applies them in front of any implementation
- `PetStoreClient` (step 4), and `PetStoreFake` and `PetStoreScenario` (step 5)

## 3. The service implements the contract

`Resultful.OpenApi.Sample/PetsHandlers.cs` is the only hand-written API code. It never touches HTTP: it returns
Resultful values, and the types won't let it return anything the spec doesn't document.

```cs
public Task<Result<Pet, GetPetError>> GetPet(long petId, CancellationToken cancellationToken)
    => repository.Find(petId, cancellationToken)
        .Match<Pet, Result<Pet, GetPetError>>(
            pet => pet,
            _ => GetPetError.NotFound(NotFound(petId)));

public Task<Result<Pet, CreatePetError>> CreatePet(NewPet body, CancellationToken cancellationToken)
    => UniqueName(body.Name, exceptId: null, cancellationToken)
        .MapAsync(_ => repository.Add(body, cancellationToken))
        .MapError(CreatePetError.Conflict);
```

The generated endpoint is the HTTP boundary. It validates the request, calls the handler and turns each outcome into
its documented response (abridged from `PetStore.Endpoints.g.cs`):

```cs
app.MapMethods("/pets", ["POST"], static async Task<Results<Created<Pet>, ValidationProblem, ProblemHttpResult>> (
    [FromBody] NewPet body, [FromServices] IPetsHandlers __handlers, CancellationToken __cancellationToken) =>
{
    if (PetStoreValidation.Validate(body) is { } __errors)
    {
        return TypedResults.ValidationProblem(__errors);
    }

    var __result = await __handlers.CreatePet(body, __cancellationToken);
    return __result.Match<...>(
        __success => TypedResults.Created((string?)null, __success),
        __error => __error.Match<...>(
            __case => TypedResults.ValidationProblem(__case.Body.Errors, ...),   // 400
            __case => Problem(__case.Body, 409)));                              // 409, as application/problem+json
})
    .WithName("createPet")
    .Produces<ProblemDetails>(409, "application/problem+json")
    .WithTags("pets");
```

The pets are stored in SQLite through EF Core, in `PetRepository.cs` and `Data/`. The starting data is a named **seed
scenario**:

| Seed | Data |
|:--|:--|
| `Empty` | no pets |
| `Demo` (the default) | Rex (dog), Tom (cat, 7 lives), Bella (dog, adopted) |
| `Crowded` | 150 pets |

Each test gets its own in-memory database from the seed it names: `new PetStoreApp("Demo")`.

### A change, start to finish: the 409 on `upsertPet`

This is how the spec drives the code. Once pet names became unique in the database, `PUT` could clash with another
pet's name, but `upsertPet` documented no error, so its handler had no way to report one.

1. **The spec changed:** a `409` with `ProblemDetails` was added to `upsertPet`.
2. **The contract changed:**
   - the handler's type went from `Task<UpsertPetSuccess>` to `Task<Result<UpsertPetSuccess, UpsertPetError>>`
   - `UpsertPetError` gained a `Conflict` case
3. **The compiler pointed at the work.** `PetsHandlers.UpsertPet` no longer compiled until it returned the new type.
   The endpoint, client and fake were regenerated to match.
4. **Tests pinned it down.** `UpsertPet_taking_another_pet_s_name_*` exists in both the black-box suite and the
   contract suite. Outcome coverage now includes `upsertPet 409`, so deleting those tests fails the build.

## 4. Proving the service matches the spec

Six independent checks, each in the test project, each broken on purpose at least once to prove it can fail:

| Check | Where | What it caught or would catch |
|:--|:--|:--|
| **Compiler** | the build | a handler not handling a documented outcome; returning an undocumented one |
| **Request validation** | generated endpoint, `ValidatingPetsHandlers` | an empty name, a bad tag, 12 lives; the same answer in-process and over HTTP |
| **Round trip** | `SpecDriftTests` | an extra hand-written endpoint (`GET /health: served, not in the spec`); a wrong constraint (`Pet.name maxLength: spec has '50', served '51'`) |
| **Contract suite** | `ContractSuite`, 10 tests × 2 targets | behaviour that differs between the handlers and the running service |
| **Outcome coverage** | `ContractSuite`, after the HTTP run | removing the only `upsertPet 409` test: `11/12 documented outcomes reached … {"upsertPet 409"}` |
| **Black box** | `RegressionSuite`, 14 tests | anything visible over HTTP, read as raw JSON, without the generated types |

**The contract suite** is written once against `IPetsHandlers`. Its two targets are the real handlers in-process (with
`ValidatingPetsHandlers` in front, no HTTP) and `PetStoreClient` against the running service. The same assertions
passing on both shows the HTTP layer adds nothing and loses nothing:

```cs
[TestFixture("in-process")]
[TestFixture("http")]
public sealed class ContractSuite(string target)
{
    [Test]
    public async Task CreatePet_with_a_taken_name_is_the_conflict_case()
    {
        var result = await Start("Demo").CreatePet(Dog("rex"), default);

        Case<CreatePetConflict>(Failure(result)).Body.Title.Should().Be("Name taken");
    }
}
```

**The round trip** compares ASP.NET Core's own `/openapi/v1.json` for the running service with `petstore.yaml`,
schema by schema and response by response, with no differences excused. It is what found that error responses were
being sent as `application/json` while the spec said `application/problem+json`.

## 5. Calling it: the typed client

`PetStoreClient` implements `IPetsHandlers` over HTTP, so a caller handles a 404 as a case, not a status code:

```cs
var pets = new PetStoreClient(new HttpClient { BaseAddress = new Uri("http://localhost:5000/") });

var outcome = await pets.GetPet(7, cancellationToken);
outcome.Switch(
    pet => Console.WriteLine(pet.Name),
    error => error.Switch(notFound => Console.WriteLine(notFound.Body.Detail)));
```

Every response is checked against the spec. These failures come from `ClientConformanceTests`, which points the client
at canned responses:

```
getPet returned 500: the spec does not document this status
getPet returned 404: the content type is 'application/json', the spec says 'application/problem+json'
getPet returned 200: the body breaks the schema: name: Must be at least 1 characters.; details.livesLeft: Must be at most 9.
getPet returned 200: the body does not match the schema: ... details ...
```

Each of these throws `ContractViolationException`. So any test that calls a service through the client is also a
conformance test of that service, including a service not written in .NET.

## 6. Faking it

The fake is generated from the same spec, so it exists as soon as the spec does: consumers can build against it before
the service is written.

### In-process

`PetStoreFake` implements `IPetsHandlers`. Each operation answers with the spec's example, or a sample built to pass the
schema, until scripted:

```cs
var fake = new PetStoreFake();
await fake.GetPet(1, default);    // Rex, from getPet's example
await fake.ListPets(null, default);  // [{ id: 1, name: "x", ... }], a generated sample that passes the schema
```

### A fluent fake server

`PetStoreFakeServer` runs the generated endpoints over the fake on a real port. It's set up with `Given` and checked
with `Should()`, typed from the spec (`FluentFakeServerTests`):

```cs
await using var server = await PetStoreFakeServer.StartAsync();

server.Given.GetPet(petId: 7).Returns.Ok(rex).Then.NotFound(new ProblemDetails { Title = "Adopted" });
server.Given.CreatePet(body: newRex).Returns.Conflict(new ProblemDetails { Title = "Rex exists" });
server.Given.NothingElse();

var pets = server.CreateClient();
// ... exercise the code under test ...

server.Should().HaveReceived.GetPet(petId: 7).Twice();
server.Should().HaveReceivedInOrder(c => c.CreatePet(), c => c.GetPet(petId: 7));
server.Should().HaveReceivedNoOtherCalls();
```

How it protects you:
- **`Returns` offers only documented answers.** For `getPet` that is `Ok` and `NotFound`; there is no way to make the
  fake return a 500.
- **Bodies are checked when given.** `Returns.Ok(rex with { Name = "" })` throws `The body breaks the schema: name:
  Must be at least 1 characters.`
- **Failures say what happened:**

  ```
  Expected the fake to have received getPet(petId: 7) once, but it received it 0 times.
  It received:
    1. getPet(petId: 1)
    2. deletePet(petId: 7)
  ```

### Scenarios, in code or JSON

A scenario says which calls to expect, in what order, what each request must contain, and what to answer.
`Scenarios/adopt-rex.json` in the test project:

```json
{
  "order": "InOrder",
  "steps": [
    { "operation": "createPet", "expect": { "body": { "name": "Rex", "details": { "kind": "dog" } } }, "status": 201,
      "body": { "id": 7, "name": "Rex", "status": "available", "details": { "kind": "dog", "goodBoy": true } } },
    { "operation": "getPet", "expect": { "petId": 7 }, "status": 200, "times": 2, "body": { "...": "..." } },
    { "operation": "deletePet", "expect": { "petId": 7 }, "status": 204 },
    { "operation": "getPet", "expect": { "petId": 7 }, "status": 404,
      "body": { "title": "Not found", "detail": "There is no pet 7." } }
  ]
}
```

The same scenario in code:

```cs
PetStoreScenario.InOrder()
    .CreatePet(expect: new { body = new { name = "Rex" } }, status: 201)
    .GetPet(expect: new { petId = 7 }, times: 2)
    .DeletePet(expect: new { petId = 7 })
    .GetPet(expect: new { petId = 7 }, status: 404)
    .Build();
```

A scenario is checked against the spec before it runs. `Scenarios/broken.json` gets five problems, all reported at
once:

```
step 1 (adoptPet → 200): the spec has no operation 'adoptPet'
step 2 (getPet → 500): the spec documents no 500 response for 'getPet'
step 3 (getPet → 200): 'id' is not a parameter; expected one of petId
step 4 (getPet → 200): The body breaks the schema: name: Must be at least 1 characters.; details.livesLeft: Must be at most 9.
step 5 (listPets → 200): times must be at least 1
```

### The fake server, from any language

`dotnet run --project Resultful.OpenApi.Sample.Fake` starts the fake server. The rest of this section is a session
recorded with curl against the real process, with `type` and `traceId` trimmed from the problems.

It answers with the spec's example, and with the real validation:

```
$ curl localhost:5080/pets/1
{"id":1,"name":"Rex","tag":"good-dog","status":"available","details":{"kind":"dog","goodBoy":true}}

$ curl -X POST localhost:5080/pets -d '{"name":"","details":{"kind":"cat","livesLeft":12}}' -H 'content-type: application/json'
{"title":"One or more validation errors occurred.","status":400,
 "errors":{"name":["Must be at least 1 characters."],"details.livesLeft":["Must be at most 9."]}}
```

It can be scripted, but only with documented outcomes:

```
$ curl -X PUT localhost:5080/__fake/getPet -d '{"status":404,"body":{"title":"Gone","detail":"No pet 7"}}' -H 'content-type: application/json'
204
$ curl -i localhost:5080/pets/7
HTTP/1.1 404 Not Found
Content-Type: application/problem+json
{"title":"Gone","status":404,"detail":"No pet 7"}

$ curl -X PUT localhost:5080/__fake/getPet -d '{"status":500}' -H 'content-type: application/json'
{"title":"Not Found","status":404,"detail":"The spec documents no 500 response for 'getPet'."}
```

It stands up an isolated fake API per scenario:

```
$ curl -X POST localhost:5080/__fake/sessions -H 'content-type: application/json' -d '{"order":"InOrder","steps":[
    {"operation":"createPet","expect":{"body":{"name":"Rex"}},"status":201},
    {"operation":"getPet","expect":{"petId":1},"status":200}]}'
{"id":"4a39f51592b9","baseUrl":"/sessions/4a39f51592b9/"}

$ curl localhost:5080/sessions/4a39f51592b9/pets/1
{"title":"Fake expectation failed","status":500,
 "detail":"getPet was called out of order: step 2 (getPet → 200 expecting {\"petId\":1}) comes after step 1 (createPet → 201 expecting {\"body\":{\"name\":\"Rex\"}})"}

$ curl localhost:5080/__fake/sessions/4a39f51592b9
{"passed":false,"failures":["getPet was called out of order: ..."],"calls":[...]}
```

The `/__fake` control API is left out of the fake's OpenAPI document. That document passes the same round-trip
comparison as the real service's (`FakeTests`). And `FakeTests` scripts each of the 12 documented outcomes in turn,
reading every answer through the conformance-checking client, so the fake can't give an answer the real API never
would.

## 7. Owning it: the scaffolded server

The fake is the spec turned into behaviour you never touch. Scaffolding goes the other way: the spec turned into
ordinary ASP.NET source you own. `Resultful.OpenApi.Sample.Scaffolded` is the Pet Store as the scaffolder writes it,
untouched. Only its project file was written by hand.

```
$ dotnet run --project Resultful.OpenApi.Scaffold -- Resultful.OpenApi.Sample.Contract/petstore.yaml \
    Resultful.OpenApi.Sample.Scaffolded --api PetStore --contract-namespace PetStore.Api --namespace PetStore.Scaffolded
  wrote    Controllers/PetsController.cs
  wrote    PetStoreConventions.cs
  wrote    PetStoreOpenApi.cs
  wrote    PetsHandlers.cs
  wrote    Program.cs
```

The controller is the shape most ASP.NET teams already write, with attributes describing exactly the documented
responses:

```cs
[ApiController]
[Tags("pets")]
public sealed class PetsController(IPetsHandlers handlers) : ControllerBase
{
    [HttpGet("/pets/{petId}", Name = "getPet")]
    [ProducesResponseType<Pet>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    public async Task<IActionResult> GetPet([FromRoute(Name = "petId")] long petId, CancellationToken cancellationToken)
    {
        var result = await handlers.GetPet(petId, cancellationToken);
        return result.Match<IActionResult>(
            success => Ok(success),
            error => error.Match<IActionResult>(@case => ProblemResponse(@case.Body, 404)));
    }
    // ...
}
```

`PetsHandlers.cs` is the reference implementation. Each operation answers with the spec's example as real C#, with a
TODO, so the service runs and conforms before anyone writes a line:

```cs
// TODO: replace this reference answer (the spec's 200 Ok) with the real behaviour.
Pet answer = new Pet { Id = 1, Name = "Rex", Tag = "good-dog", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } };
```

The scaffolded project also opts in to compile-time verification (`ResultfulParts="Verify"` on the spec). From now
on, its build checks the hand-written controllers against the spec. This is what breaking the controller five ways at
once does:

```
petstore.yaml(1,1): error RAPI010: The spec's operation 'deletePet' (DELETE /pets/{petId}) has no endpoint: add a controller action or Map call for it
PetsController.cs(82,6): error RAPI011: PetsController.Health (GET /health) is not in the spec: document it, or leave it out of the OpenAPI description (...)
PetsController.cs(51,6): error RAPI012: PetsController.GetPet (GET /pets/{petId}): the spec documents a 404 response, but the endpoint declares none; declare it with [ProducesResponseType]
PetsController.cs(51,6): error RAPI013: PetsController.GetPet (GET /pets/{petId}): the endpoint declares a 410 response, which the spec does not document
PetsController.cs(51,6): error RAPI014: PetsController.GetPet (GET /pets/{petId}) is named 'fetchPet'; name it 'getPet', the spec's operationId, which the OpenAPI description uses
PetsController.cs(63,6): error RAPI015: PetsController.UpsertPet (PUT /pets/{petId}): the route parameter is '{id}', the spec calls it '{petId}'
```

It checks the wiring too, using ASP.NET Core's own binding rules. Binding `limit` as a non-nullable `int`,
`petId` as `int` rather than `long`, the body from the query, and an undocumented `hard` flag gives:

```
PetsController.cs(23,6): error RAPI019: PetsController.ListPets (GET /pets): 'limit' is optional in the spec, but binding it as a non-nullable int with no default makes it required; make it int? or give it a default
PetsController.cs(51,6): error RAPI018: PetsController.GetPet (GET /pets/{petId}): 'petId' is bound as int; the spec says long
PetsController.cs(32,6): error RAPI020: PetsController.CreatePet (POST /pets): the spec has a NewPet request body, but the endpoint binds none
PetsController.cs(32,6): error RAPI017: PetsController.CreatePet (POST /pets): 'body' binds query value 'body', which the spec doesn't document
PetsController.cs(83,6): error RAPI017: PetsController.DeletePet (DELETE /pets/{petId}): 'hard' binds query value 'hard', which the spec doesn't document
```

The models are checked as well. The scaffold uses the generated `Pet` and `NewPet`, so they match the spec by
construction and nothing is reported. A team that swaps in its own types gets them compared with the schemas, as
System.Text.Json would write them. A hand-written `PetDto` that matches is noted (`RAPI021`, info), and one that drifts
is reported at each JSON path where it differs (`RAPI022`):

```
PetsController.cs(32,6): info RAPI021: PetsController.CreatePet (POST /pets): the 201 response is PetDto, which matches the schema Pet today; use the generated types so it stays consistent by construction
PetsController.cs(51,6): error RAPI022: PetsController.GetPet (GET /pets/{petId}): the 200 response is NewPet, which differs from the schema Pet: id is missing
PetsController.cs(23,6): error RAPI022: PetsController.ListPets (GET /pets): the 200 response is List<DriftedPet>, which differs from the schema Pet[]: [].id is int (integer/int32); the schema says integer/int64
PetsController.cs(23,6): error RAPI022: PetsController.ListPets (GET /pets): the 200 response is List<DriftedPet>, which differs from the schema Pet[]: [].details is discriminated by 'type'; the schema uses 'kind'
PetsController.cs(63,6): error RAPI022: PetsController.UpsertPet (PUT /pets/{petId}): the request body is PetDto, which differs from the schema NewPet: id (Id) is not in the schema
```

So a team can own and edit every line of the HTTP layer, and the compiler still guarantees it covers the spec, and
only the spec, down to how each value reaches the handler and the shape of every model it reads and writes.

Re-running the scaffolder keeps every file that exists unless given `--force`. So after a spec change it only adds
what is new, and the compiler shows which existing handlers no longer fit the contract.

What the tests show about the untouched scaffold:
- **`ScaffoldedServerTests`:**
  - the controllers' OpenAPI document matches the spec exactly
  - every operation's reference answer passes the conformance-checking client
  - an invalid `NewPet` gets the spec's validation errors (`name`, `tag`, `details.livesLeft`)
- **`ScaffoldTests`:**
  - the scaffolder reproduces the committed files byte for byte
  - it keeps files that exist unless forced
  - its Minimal API style builds in a fresh project, with `Verify` on and warnings as errors

## What tells you what

| You... | What tells you |
|:--|:--|
| add an error response to the spec | the compiler, in every handler and consumer that matches on that operation's errors |
| forget to handle a documented outcome in a handler | the compiler |
| add an endpoint by hand that the spec doesn't have | `SpecDriftTests` |
| change a constraint in code, not the spec | `SpecDriftTests` |
| send a response the spec doesn't allow | `PetStoreClient`, in any test that calls the service |
| write handlers that differ from the HTTP behaviour | `ContractSuite`, which runs on both targets |
| leave a documented outcome untested | outcome coverage in `ContractSuite` |
| make the fake answer something the API never would | refused when scripted; `FakeTests` checks every outcome it can give |
| call the fake in the wrong order or with the wrong request | the scenario, or `Should().HaveReceivedInOrder(...)` |
| write a scenario file with a typo | `PetStoreFake.Check`, when it's loaded |
| remove, rename or misdeclare a hand-written endpoint | the build (`RAPI010`–`RAPI015`), in projects that opt in to `Verify` |
| bind a parameter or body from the wrong place, with the wrong type, or as required when it's optional | the build (`RAPI016`–`RAPI020`) |
| use a model whose JSON or constraints differ from the schema, or a hand-written one where the generated one would do | the build (`RAPI022`, and `RAPI021` as info) |
| constrain a parameter differently from the spec (`[Range]`, `[MaxLength]`) | the build (`RAPI023`) |
| scaffold a server whose shape drifts from the spec | `ScaffoldedServerTests` (the round trip, on controllers) |

## Where things are

| File | What it is |
|:--|:--|
| `Resultful.OpenApi.Sample.Contract/petstore.yaml` | the spec |
| `Resultful.OpenApi.Sample/PetsHandlers.cs` | the only hand-written API logic |
| `Resultful.OpenApi.Sample/PetRepository.cs`, `Data/` | SQLite through EF Core, and the seed scenarios |
| `Resultful.OpenApi.Sample/Program.cs` | the service's host |
| `Resultful.OpenApi.Sample.Fake/Program.cs` | the fake server's host |
| `Resultful.OpenApi.Sample.Tests/ContractSuite.cs` | one suite, two targets, outcome coverage |
| `Resultful.OpenApi.Sample.Tests/RegressionSuite.cs` | the black-box suite |
| `Resultful.OpenApi.Sample.Tests/SpecDriftTests.cs` | the round trip |
| `Resultful.OpenApi.Sample.Tests/ClientConformanceTests.cs` | the client catching a non-conforming server |
| `Resultful.OpenApi.Sample.Tests/FakeTests.cs` | the fake, in-process and as a server |
| `Resultful.OpenApi.Sample.Tests/FluentFakeServerTests.cs` | `Given` and `Should()` on a real port |
| `Resultful.OpenApi.Sample.Tests/ScenarioTests.cs`, `Scenarios/` | scenarios in code and JSON, and sessions |
| `Resultful.OpenApi.Sample.Scaffolded/` | the scaffolder's untouched output: controllers and a reference implementation |
| `Resultful.OpenApi.Scaffold/Program.cs` | the scaffolding tool |
| `Resultful.OpenApi.Generator/Emitter.cs`, `EmitterScaffold.cs` | everything above that wasn't written by hand |
