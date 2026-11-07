# Resultful.OpenApi (prototype)

Spec-first ASP.NET Core on .NET 10. The OpenAPI document is the source of truth. A Roslyn source generator reads it at
build time and writes the server's contracts, and the hand-written code returns Resultful types. A regression suite and
a round-trip check against the spec decide whether the server is done.

This is a prototype. Nothing here is packed or published, and the `Resultful` package is unchanged.

**[VISION.md](VISION.md)** is where this is going and why. **[PETSTORE.md](PETSTORE.md)** walks through the sample end to end. **[CONCEPT.md](CONCEPT.md)** sets out the idea and its three uses: the workflow for spec-first services, a testing
framework that checks any API against its spec, and a generated fake server. This README covers the mechanics.

```
petstore.yaml ──[Resultful.OpenApi.Generator]──▶ DTOs, response unions, IPetsHandlers, validation, MapPetStore()
     ▲                                                         │
     │                                          PetsHandlers.cs (hand-written, Resultful)
     │                                                         ▼
 SpecDriftTests ◀──── /openapi/v1.json (ASP.NET Core's own) ── running app ◀── RegressionSuite (HTTP)
```

## Run it

```bash
cd prototypes/openapi
dotnet test Resultful.OpenApi.slnx
```

The generated files are written to `obj/generated/` in the Contract and Sample projects, so you can read them.

| Project | What it is |
|---|---|
| `Resultful.OpenApi.Generator` | The `IIncrementalGenerator` (netstandard2.0). It reads the spec with Microsoft.OpenApi 3.x, which ships inside the analyzer. |
| `Resultful.OpenApi.Sample.Contract` | `petstore.yaml` and the generated **Contract** part: types, response unions, `IPetsHandlers`, validation, `ValidatingPetsHandlers` and `PetStoreClient`. What a consumer or a test suite references. |
| `Resultful.OpenApi.Sample` | The service: the generated **Server** part (endpoints, OpenAPI configuration), `Program.cs`, the hand-written handlers (`PetsHandlers.cs`), and storage in SQLite through EF Core (`PetRepository.cs`, `Data/`). |
| `Resultful.OpenApi.Fakes` | The runtime for generated fakes: `Scenario` (expected calls, in code or JSON), `ScenarioRunner` (order, request expectations, verification), `FakeCall`. Referencing it turns on the generated fake and scenario builder. |
| `Resultful.OpenApi.Scaffold` | The scaffolding tool: writes the server side of a spec as owned ASP.NET source (controllers or Minimal API), with a reference implementation. Never overwrites without `--force`. |
| `Resultful.OpenApi.Sample.Scaffolded` | The Pet Store as the scaffolder writes it, untouched; tested to match the spec before anyone edits it. |
| `Resultful.OpenApi.Sample.Fake` | A fake PetStore server: the generated **Server** part over the generated fakes, in a ten-line `Program.cs`. `dotnet run`, then script the default fake with `PUT /__fake/{operationId}`, or stand up an isolated fake from a scenario with `POST /__fake/sessions`. |
| `Resultful.OpenApi.Sample.Tests` | `ContractSuite` (one suite against `IPetsHandlers`, run in-process and over HTTP, with outcome coverage), `ClientConformanceTests` (the client catching a server that breaks the spec), `RegressionSuite` (raw HTTP and JSON), `HandlerTests`, `SpecDriftTests` (the round trip). Every server test starts from a named seed scenario. |
| `Resultful.OpenApi.Generator.Tests` | The generator on small specs: what it emits, and the diagnostics for what it doesn't support. |

## What the spec becomes

A spec is enabled with an item in the project file:

```xml
<AdditionalFiles Include="petstore.yaml" ResultfulOpenApi="true" ResultfulNamespace="PetStore.Api" ResultfulApiName="PetStore" />
```

Each operation's handler signature comes from its responses:

| Responses in the spec | Handler returns | Example |
|---|---|---|
| one 2xx with a body, no errors | `Task<T>` | `listPets` → `Task<IReadOnlyList<Pet>>` |
| one 2xx with a body, plus errors | `Task<Result<T, {Op}Error>>` | `getPet` → `Task<Result<Pet, GetPetError>>` |
| 204, plus errors | `Task<VoidResult<{Op}Error>>` | `deletePet` |
| more than one 2xx | `Task<{Op}Success>`, or `Result<{Op}Success, {Op}Error>` | `upsertPet` (200 replaced / 201 created, or 409) |

- **The error and success unions** are generated `OneOfBase` classes, with one case record per documented response
  (`GetPetNotFound(ProblemDetails Body)`). Each has static factories (`GetPetError.NotFound(problem)`) because C#
  won't chain implicit conversions from a case to the union to the `Result`.
- **The endpoint** is a Minimal API lambda returning `Results<...>` of the documented responses. It calls the handler
  and maps the result with `Match`, so a response the spec doesn't document can't be returned, and a documented one
  can't be forgotten.
- **Request validation** is generated from the schema's `required`, `minLength`, `maxLength`, `pattern`, `minimum` and
  `maximum`, recursing into `$ref`s and oneOf members. It runs before the handler and returns
  `ValidationProblem` (400). A documented 400 whose body is `HttpValidationProblemDetails` is also a case of the
  handler's error union (`CreatePetBadRequest`), so handlers can return domain validation failures in the same shape.
  `ValidatingPetsHandlers` applies the same validation in front of any implementation of the interface.
- **The client** (`PetStoreClient`) implements the handler interfaces over HTTP and returns the same types. It checks
  every response's status, content type and schema, throwing `ContractViolationException` for anything the spec
  doesn't allow, and records which documented outcomes it received (`Observed`, against
  `PetStoreContract.DocumentedOutcomes`).
- **The fake** (`PetStoreFake`) implements the handler interfaces with the spec's examples, or samples built to pass the
  schema. It is scripted with `On{Operation}(...)`, `Respond(operationId, status, json)` or a scenario of expected
  calls (`PetStoreScenario`, or JSON), and records calls. `Add{Api}FakeHub`, `Use{Api}FakeHub` and
  `Map{Api}FakeControl` host it as a fake server with isolated sessions (the `FakeServer` part). The fake is generated
  only when the project references `Resultful.OpenApi.Fakes`.
- **The fluent fake** (`fake.Given...`, `fake.Should()...`) and **`PetStoreFakeServer`**, a real server on a free port
  for tests. See CONCEPT.md.
- **Parts.** `ResultfulParts="Contract"` generates what callers need; `"Server"` generates the endpoints and OpenAPI
  configuration; `"Server,FakeServer"` adds fake hosting (the hub, the control API and `PetStoreFakeServer`); `"Verify"` generates
  nothing and checks the project's hand-written controllers and Map calls against the spec (`RAPI010`–`RAPI015`). The
  default is `Contract,Server`. Separate parts with commas: the value reaches the generator through an `.editorconfig`,
  where `;` starts a comment.
- **Schemas** become `record`s with `required` init properties. String enums become enums with
  `JsonStringEnumMemberName`. A discriminated `oneOf` becomes an abstract record with `[JsonPolymorphic]`.
  `ProblemDetails` and `HttpValidationProblemDetails` map to the framework types.
- **`AddPetStore()`** sets the JSON options the spec implies: strict number handling, and a discriminator may come
  anywhere in the object.

## Storage and seed scenarios

Pets are stored in SQLite through EF Core 10. `PetRecord` is the table and the generated `Pet` is the API: the spec
owns one, the database the other. Names are unique, ignoring case (a `NOCASE` collation and a unique index). The
oneOf `details` are stored as the same JSON the API uses.

The starting data is a **seed scenario**, chosen by name with `PetStore:Seed` and run through EF Core's `UseSeeding`
and `UseAsyncSeeding` when the database is created:

| Scenario | Data | Used for |
|---|---|---|
| `Empty` | no pets | creating, validation, upserting a new id |
| `Demo` (the default) | Rex (dog), Tom (cat, 7 lives), Bella (dog, adopted) | reads, conflicts, deletes |
| `Crowded` | 150 pets | `listPets`' default page of 100 |

Adding a scenario is one class deriving from `SeedScenario`, registered in `AddPetStoreDatabase`. The connection
string is `ConnectionStrings:PetStore`, with `Data Source=petstore.db` as the default. Tests use `PetStoreApp(seed)`,
which gives each test its own shared in-memory database (`Mode=Memory;Cache=Shared`, held open by `SqliteKeepAlive`).
The schema comes from `EnsureCreated`; a real service would use migrations, and those run the same seeding.

## Assertions

The tests use [AwesomeAssertions](https://awesomeassertions.org) 9.6, the Apache-2.0 fork of FluentAssertions, which
needs a commercial licence from v8. One setting matters: AwesomeAssertions compares a member by its declared type by
default, so a difference inside a oneOf member such as `PetDetails` passes `BeEquivalentTo` silently.
`AssertionSetup.cs` switches the test assembly to runtime member types.

The first version of this prototype used FluentCheck 0.1.0. It was dropped because its deep comparisons report actual
and expected the wrong way round and almost none of its assertions chain.

## What the prototype found

1. **The round trip works and catches real drift.** ASP.NET Core 10's own OpenAPI document of the generated server is
   compared with the input spec: operations, operationIds, parameters, content types, response codes, body schemas,
   and every component schema's properties, types, formats and constraints. Its first run found four gaps, and the
   generator now fixes the first three:
   - Constraints were missing, because validation is generated code, not attributes. The generator now also emits
     DataAnnotations, used only so the document carries the constraints.
   - Integers were documented as `integer | string`, because the web JSON defaults read numbers from strings. Fixed
     with strict number handling in `AddPetStore()`.
   - ProblemDetails responses were documented as `application/json`, and they were also sent that way:
     `TypedResults.NotFound(problem)` writes JSON. Problem responses now go out through `TypedResults.Problem`, which
     writes `application/problem+json`, with the status forced to the one in the spec, and each status is documented
     with `.Produces<ProblemDetails>(status, "application/problem+json")`. Fixing only the document had hidden the
     wire bug; the regression suite's content-type check found it.
   - Polymorphism was written differently. ASP.NET Core names oneOf members `{Union}{Member}` (`PetDetailsDog`),
     writes the union as `anyOf` with a `type` and `required` of its own, writes the discriminator as a single-value
     enum left out of `required`, and leaves `type: string` off string enums. The generator now emits
     `AddPetStoreSchemas()` for `OpenApiOptions` (only when the app references Microsoft.AspNetCore.OpenApi). It names
     schemas after their types and rewrites each spec union, its members and string enums into the spec's form,
     driven by the spec's own unions and enums. The drift test now compares every schema strictly, with no excused
     differences. Without the transformer it reports all ten differences.

     One part of that is by construction, not checked: the transformer writes each member's discriminator as a
     required string from the spec, rather than reading it from the code.

   Checked by breaking it on purpose: an extra hand-written endpoint fails the test (`GET /health: served, not in the
   spec`), and so does a wrong generated constraint (`Pet.name maxLength: spec has '50', served '51'`).
2. **The regression suite catches handler bugs the compiler can't.** Returning 200 where the spec means 201
   type-checks, because both are documented cases, but it fails both the HTTP test and the handler test.
3. **The contract caught a gap in the spec.** Once names were unique in the database, `PUT` could hit a name clash,
   but `upsertPet` documented no error, so its handler had no way to report one. Adding a 409 to the spec changed the
   generated contract to `Result<UpsertPetSuccess, UpsertPetError>`, and the compiler pointed at the handler until it
   dealt with it.
4. **Generators can't see each other's output.** The generated endpoints are invisible to the Request Delegate
   Generator, the .NET 10 Minimal API validation generator and the System.Text.Json generator. So the prototype uses
   reflection-based binding and JSON, and generates its own validation. Supporting Native AOT would mean the generator
   writing request delegates and a `JsonSerializerContext` itself.
5. **Bundling the spec reader works.** Microsoft.OpenApi 3.11, its YAML reader and SharpYaml ship next to the
   generator, while the app's Microsoft.AspNetCore.OpenApi uses Microsoft.OpenApi 2.x. The compiler loads each
   analyzer in its own context, so the two versions don't clash. System.Text.Json comes from the .NET 10 compiler host.
   Visual Studio's .NET Framework compiler host has not been tried.
6. **Resultful fits as the handler contract,** with these gaps:
   - `Result<T, TError>` has one success case, so operations with several 2xx responses need a generated success
     union.
   - `Result<T>` and `VoidResult` (string errors) are not used in generated contracts.
   - `default(Result<,>)` is not checked. OneOf's default is case 0, so a default `Result` would read as a success
     holding `null`.
7. **The published test helpers are stale.** FluentAssertions.Resultful 0.0.6-beta01 is built on FluentAssertions 5.6
   and Resultful 0.2.0-beta04. FluentAssertions.OneOf 0.0.5 is built on OneOf 1.0 and doesn't understand `OneOfBase`.
   Resultful/FluentAssertions.Resultful is being renamed to `Resultful.AwesomeAssertions`, with the FluentAssertions 7
   packages kept as a legacy build (Resultful/FluentAssertions.Resultful#7). `OneOfBase` support, which the generated
   unions here need, is Resultful/FluentAssertions.Resultful#2.

## Not supported yet

The generator reports these as `RAPI002` warnings and skips them; it never emits code that doesn't compile:

- inline object schemas (use a component `$ref`)
- `allOf`
- `oneOf` without a discriminator
- form and multipart bodies
- cookie parameters
- `default` responses
- status codes outside the table in `Naming.StatusNames`
- more than six responses on one operation (`Results<...>` takes at most six)

A generator package would also need a `.props` file declaring the `CompilerVisibleItemMetadata`; the sample declares
it by hand.

## Next steps, if this goes further

- A Schemathesis run against the sample in CI, adding property-based coverage on top of the hand-written suite.
- Trying the generator on a large real spec, to find the next unsupported constructs.
- Deciding whether a success union belongs in Resultful itself.
- Use `Resultful.AwesomeAssertions.OneOf` in these tests in place of the `Case<T>` helper, once it supports
  `OneOfBase` (Resultful/FluentAssertions.Resultful#2).
- Migrations in place of `EnsureCreated`, with the seed scenarios kept as they are.
