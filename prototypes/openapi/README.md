# Resultful.OpenApi (prototype)

Spec-first ASP.NET Core on .NET 10. The OpenAPI document is the source of truth. A Roslyn source generator reads it at
build time and writes the server's contracts, and the hand-written code returns Resultful types. A regression suite and
a round-trip check against the spec decide whether the server is done.

This is a prototype. Nothing here is packed or published, and the `Resultful` package is unchanged.

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

The generated files are written to `Resultful.OpenApi.Sample/obj/generated/` so you can read them.

| Project | What it is |
|---|---|
| `Resultful.OpenApi.Generator` | The `IIncrementalGenerator` (netstandard2.0). It reads the spec with Microsoft.OpenApi 3.x, which ships inside the analyzer. |
| `Resultful.OpenApi.Sample` | `petstore.yaml`, `Program.cs`, the hand-written handlers (`PetsHandlers.cs`), and storage in SQLite through EF Core (`PetRepository.cs`, `Data/`). |
| `Resultful.OpenApi.Sample.Tests` | `RegressionSuite` (HTTP only, through `WebApplicationFactory`), `HandlerTests` (no HTTP), `SpecDriftTests` (the round trip). Every test starts from a named seed scenario. |
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
  `ValidationProblem` (400). A 400 whose body is `HttpValidationProblemDetails` belongs to the generated endpoint and
  is not part of the handler's error union. Any other 400 is a normal handler error.
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

The tests use [FluentCheck](https://www.nuget.org/packages/FluentCheck) 0.1.0 (MIT) rather than FluentAssertions,
which needs a commercial licence from v8. Its `Should()` API mostly matches FluentAssertions, and its failure messages
quote the asserted expression (`Expected response.StatusCode to be ...`). The differences that mattered here:

- `BeOfType<T>()`, `ContainSingle()` and most collection assertions return `void`, so they can't be chained, and
  `BeOfType` doesn't hand back the typed subject. The tests' `Case<T>` helper asserts and then casts.
- String collections use `BeInExactOrder([...])` rather than `Equal(...)`. It fails on a prefix and on a different
  order, which I checked.
- Nullable numbers have no `Be`, so the tests compare `(x ?? 0)`.
- There is no `because` argument on `Be`.

It is a single 0.1.0 release that targets only .NET 10, so it is worth checking how actively it's maintained before
moving Resultful's own tests to it. Those still use FluentAssertions 7.2, which is under the older free licence.

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
   - Polymorphism is written differently: members are named `PetDetailsDog`, the union is written as `anyOf`, and the
     discriminator is a single-value enum. That is ASP.NET Core's representation, not an API difference.
     `SpecComparer.KnownDifferences` names each one and undoes it, and hides nothing else. Fixing it would take an
     OpenAPI schema transformer.

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
   Either way, a Resultful assertion package would now need building on another library, such as FluentCheck.

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

- An OpenAPI schema transformer, so polymorphism round-trips without known differences.
- A Schemathesis run against the sample in CI, adding property-based coverage on top of the hand-written suite.
- Trying the generator on a large real spec, to find the next unsupported constructs.
- Deciding whether a success union belongs in Resultful itself.
- A Resultful assertion package for FluentCheck (or whichever library wins), in place of FluentAssertions.Resultful.
- Migrations in place of `EnsureCreated`, with the seed scenarios kept as they are.
