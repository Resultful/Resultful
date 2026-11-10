using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The Verify part: the build checks hand-written endpoints (controller actions, Minimal API Map calls) against the spec.
/// ASP.NET Core's attributes and Map methods are stood in for by same-named types, which is all the verifier reads.
/// </summary>
[TestFixture]
public sealed class CoverageTests
{
    private const string Spec = """
        openapi: 3.1.0
        info: { title: T, version: '1' }
        paths:
          /things/{thingId}:
            get:
              operationId: getThing
              parameters: [{ name: thingId, in: path, required: true, schema: { type: integer } }]
              responses:
                '200': { description: ok, content: { application/json: { schema: { type: string } } } }
                '404': { description: missing }
            delete:
              operationId: deleteThing
              parameters: [{ name: thingId, in: path, required: true, schema: { type: integer } }]
              responses:
                '204': { description: gone }
        """;

    internal const string AspNet = """
        global using Contract;
        namespace Contract { }
        namespace Microsoft.AspNetCore.Mvc
        {
            using System;
            public sealed class ApiControllerAttribute : Attribute { }
            public sealed class RouteAttribute(string template) : Attribute { }
            public sealed class HttpGetAttribute(string template = "") : Attribute { public string? Name { get; set; } }
            public sealed class HttpDeleteAttribute(string template = "") : Attribute { public string? Name { get; set; } }
            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            public sealed class ProducesResponseTypeAttribute(int statusCode) : Attribute { }
            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            public sealed class ProducesResponseTypeAttribute<T>(int statusCode, string contentType) : Attribute { }
            public sealed class NonActionAttribute : Attribute { }
            public sealed class ApiExplorerSettingsAttribute : Attribute { public bool IgnoreApi { get; set; } }
            public sealed class HttpPostAttribute(string template = "") : Attribute { public string? Name { get; set; } }
            public sealed class FromRouteAttribute : Attribute { public string? Name { get; set; } }
            public sealed class FromQueryAttribute : Attribute { public string? Name { get; set; } }
            public sealed class FromHeaderAttribute : Attribute { public string? Name { get; set; } }
            public sealed class FromBodyAttribute : Attribute { }
            public sealed class FromServicesAttribute : Attribute { }
            public class ControllerBase { }
            public class ProblemDetails { public string? Title { get; set; } }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }
            public class HttpValidationProblemDetails : Microsoft.AspNetCore.Mvc.ProblemDetails { }
        }

        namespace Microsoft.AspNetCore.Http.HttpResults
        {
            public sealed class Ok<T> : IResult { }
            public sealed class Created<T> : IResult { }
            public sealed class NotFound : IResult { }
            public sealed class NoContent : IResult { }
            public sealed class ProblemHttpResult : IResult { }
            public sealed class ValidationProblem : IResult { public HttpValidationProblemDetails ProblemDetails { get; } = new(); }
            public sealed class Results<T1, T2> : IResult where T1 : IResult where T2 : IResult
            {
                public static implicit operator Results<T1, T2>(T1 result) => new();
                public static implicit operator Results<T1, T2>(T2 result) => new();
            }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            using System;
            public sealed class App { }
            public sealed class Endpoint { }
            public static class EndpointRouteBuilderExtensions
            {
                public static Endpoint MapGet(this App app, string pattern, Delegate handler) => new();
                public static Endpoint MapDelete(this App app, string pattern, Delegate handler) => new();
                public static Endpoint MapPost(this App app, string pattern, Delegate handler) => new();
                public static Endpoint MapMethods(this App app, string pattern, string[] methods, Delegate handler) => new();
                public static App MapGroup(this App app, string prefix) => app;
                public static Endpoint WithName(this Endpoint endpoint, string name) => endpoint;
                public static Endpoint ExcludeFromDescription(this Endpoint endpoint) => endpoint;
                public static App ExcludeFromDescription(this App app) => app;
                public static Endpoint Produces(this Endpoint endpoint, int statusCode, Type? responseType = null, string? contentType = null) => endpoint;
                public static Endpoint Produces<T>(this Endpoint endpoint, int statusCode = 200, string? contentType = null) => endpoint;
                public static Endpoint ProducesProblem(this Endpoint endpoint, int statusCode, string? contentType = null) => endpoint;
                public static Endpoint ProducesValidationProblem(this Endpoint endpoint, int statusCode = 400, string? contentType = null) => endpoint;
                public static App ProducesProblem(this App app, int statusCode, string? contentType = null) => app;
            }
        }
        """;

    [Test]
    public void Controllers_that_cover_the_spec_exactly_pass()
    {
        Verify("""
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [Route("things")]
            public sealed class ThingsController : ControllerBase
            {
                [HttpGet("{thingId:long}", Name = "getThing")]
                [ProducesResponseType<string>(200, "application/json")]
                [ProducesResponseType(404)]
                public object Get(int thingId) => thingId;

                [HttpDelete("/things/{thingId}", Name = "deleteThing")]
                [ProducesResponseType(204)]
                public void Delete(int thingId) { }

                [HttpGet("/health")]
                [ApiExplorerSettings(IgnoreApi = true)]
                public string Health() => "ok";

                [NonAction]
                public void Helper() { }
            }
            """).Should().BeEmpty();
    }

    [Test]
    public void Every_kind_of_drift_is_reported_where_it_is()
    {
        var diagnostics = Verify("""
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class ThingsController : ControllerBase
            {
                [HttpGet("/[controller]/{id}", Name = "fetchThing")]
                [ProducesResponseType(200)]
                [ProducesResponseType(410)]
                public object Get(long id) => id;

                [HttpGet("/health")]
                public string Health() => "ok";
            }
            """);

        diagnostics.Should().Equal(
            "MM010: The spec's operation 'deleteThing' (DELETE /things/{thingId}) has no endpoint: add a controller action or Map call for it",
            "MM011: ThingsController.Health (GET /health) is not in the spec: document it, or leave it out of the OpenAPI description ([ApiExplorerSettings(IgnoreApi = true)] or ExcludeFromDescription())",
            "MM012: ThingsController.Get (GET /things/{thingId}): the spec documents a 404 response, but the endpoint declares none; declare it with [ProducesResponseType]",
            "MM013: ThingsController.Get (GET /things/{thingId}): the endpoint declares a 410 response, which the spec does not document",
            "MM014: ThingsController.Get (GET /things/{thingId}) is named 'fetchThing'; name it 'getThing', the spec's operationId, which the OpenAPI description uses",
            "MM015: ThingsController.Get (GET /things/{thingId}): the route parameter is '{id}', the spec calls it '{thingId}'");
    }

    [Test]
    public void Minimal_API_endpoints_are_checked_too_including_group_prefixes()
    {
        Verify("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http.HttpResults;

            public static class Api
            {
                public static void Map(App app)
                {
                    var things = app.MapGroup("/things");
                    things.MapGet("/{thingId}", Results<Ok<string>, NotFound> (int thingId) => new Ok<string>()).WithName("getThing");
                    app.MapMethods("/things/{thingId}", new[] { "DELETE" }, Delete).WithName("deleteThing").Produces(204);
                    app.MapGet("/health", () => "ok").ExcludeFromDescription();
                    var admin = app.MapGroup("/admin").ExcludeFromDescription();
                    admin.MapGet("/stats", () => 1);
                }

                private static void Delete(int thingId) { }
            }
            """).Should().BeEmpty();

        Verify("""
            using Microsoft.AspNetCore.Builder;

            public static class Api
            {
                public static void Map(App app)
                {
                    app.MapGet("/things/{thingId}", (int thingId) => "x");
                    app.MapGet("/version", () => "1");
                }
            }
            """).Should().Equal(
                "MM010: The spec's operation 'deleteThing' (DELETE /things/{thingId}) has no endpoint: add a controller action or Map call for it",
                "MM011: MapGet(\"/version\") (GET /version) is not in the spec: document it, or leave it out of the OpenAPI description ([ApiExplorerSettings(IgnoreApi = true)] or ExcludeFromDescription())",
                "MM012: MapGet(\"/things/{thingId}\") (GET /things/{thingId}): the spec documents a 404 response, but the endpoint declares none; declare it with a typed result or .Produces call",
                "MM014: MapGet(\"/things/{thingId}\") (GET /things/{thingId}) is named 'none'; name it 'getThing', the spec's operationId, which the OpenAPI description uses");
    }

    private const string BindingSpec = """
        openapi: 3.1.0
        info: { title: T, version: '1' }
        paths:
          /things/{thingId}/notes:
            post:
              operationId: addNote
              parameters:
                - { name: thingId, in: path, required: true, schema: { type: integer, format: int64 } }
                - { name: limit, in: query, required: false, schema: { type: integer } }
                - { name: X-Request-Id, in: header, required: true, schema: { type: string, format: uuid } }
              requestBody:
                content: { application/json: { schema: { $ref: '#/components/schemas/Note' } } }
              responses:
                '204': { description: added }
        components:
          schemas:
            Note:
              type: object
              properties: { text: { type: string } }
        """;

    // Note stands for the type generated from the schema: it's in the generated namespace, so the body is consistent by construction.
    private const string Note = """
        namespace Contract
        {
            public sealed class Note { public string? Text { get; set; } }
            public interface IClock { }
        }
        """;

    [Test]
    public void Parameters_wired_as_the_spec_says_pass_whether_explicit_or_inferred()
    {
        Verify("""
            using System;
            using System.Threading;
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class NotesController : ControllerBase
            {
                [HttpPost("/things/{thingId}/notes", Name = "addNote")]
                [ProducesResponseType(204)]
                public void Add(
                    long thingId,                                            // inferred: in the route template
                    int? limit,                                              // inferred: a simple type, from the query
                    [FromHeader(Name = "X-Request-Id")] Guid requestId,      // explicit, renamed
                    Note note,                                               // inferred: [ApiController] binds a complex type from the body
                    [FromServices] IClock clock,                             // services are not part of the spec
                    CancellationToken cancellationToken) { }                 // nor are framework types
            }
            """ + Note, BindingSpec).Should().BeEmpty();

        Verify("""
            using System;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Mvc;

            public static class Api
            {
                public static void Map(App app)
                    => app.MapPost("/things/{thingId:long}/notes", Add).WithName("addNote").Produces(204);

                private static void Add(long thingId, [FromHeader(Name = "X-Request-Id")] Guid requestId, Note note, IClock clock, int limit = 10) { }
            }
            """ + Note, BindingSpec).Should().BeEmpty();
    }

    [Test]
    public void Every_kind_of_wiring_mistake_is_reported()
    {
        var diagnostics = Verify("""
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class NotesController : ControllerBase
            {
                [HttpPost("/things/{thingId}/notes", Name = "addNote")]
                [ProducesResponseType(204)]
                public void Add(int thingId, int limit, [FromQuery] string? sort, [FromQuery] Note note) { }
            }
            """ + Note, BindingSpec);

        diagnostics.Should().Equal(
            "MM016: NotesController.Add (POST /things/{thingId}/notes): the spec's header parameter 'X-Request-Id' is not bound; add [FromHeader(Name = \"X-Request-Id\")] Guid",
            "MM017: NotesController.Add (POST /things/{thingId}/notes): 'note' binds query value 'note', which the spec doesn't document",
            "MM017: NotesController.Add (POST /things/{thingId}/notes): 'sort' binds query value 'sort', which the spec doesn't document",
            "MM018: NotesController.Add (POST /things/{thingId}/notes): 'thingId' is bound as int; the spec says long",
            "MM019: NotesController.Add (POST /things/{thingId}/notes): 'limit' is optional in the spec, but binding it as a non-nullable int with no default makes it required; make it int? or give it a default",
            "MM020: NotesController.Add (POST /things/{thingId}/notes): the spec has a Note request body, but the endpoint binds none");
    }

    [Test]
    public void Without_ApiController_a_complex_parameter_is_not_the_body()
    {
        Verify("""
            using System;
            using Microsoft.AspNetCore.Mvc;

            public sealed class NotesController : ControllerBase
            {
                [HttpPost("/things/{thingId}/notes", Name = "addNote")]
                [ProducesResponseType(204)]
                public void Add(long thingId, int? limit, [FromHeader(Name = "X-Request-Id")] Guid requestId, Note note) { }
            }
            """ + Note, BindingSpec).Should().Equal(
                "MM020: NotesController.Add (POST /things/{thingId}/notes): the spec has a Note request body, but 'note' binds from the form and query: mark it [FromBody], or add [ApiController]");
    }

    private const string ModelSpec = """
        openapi: 3.1.0
        info: { title: T, version: '1' }
        paths:
          /pets:
            get:
              operationId: listPets
              responses:
                '200': { description: ok, content: { application/json: { schema: { type: array, items: { $ref: '#/components/schemas/Pet' } } } } }
            post:
              operationId: addPet
              requestBody:
                content: { application/json: { schema: { $ref: '#/components/schemas/NewPet' } } }
              responses:
                '201': { description: added, content: { application/json: { schema: { $ref: '#/components/schemas/Pet' } } } }
        components:
          schemas:
            NewPet:
              type: object
              required: [name]
              properties:
                name: { type: string, minLength: 1, maxLength: 50 }
                tag: { type: [string, 'null'], pattern: '^[a-z-]+$' }
            Pet:
              type: object
              required: [id, name, status, details]
              properties:
                id: { type: integer, format: int64 }
                name: { type: string }
                status: { $ref: '#/components/schemas/Status' }
                details: { $ref: '#/components/schemas/Details' }
            Status:
              type: string
              enum: [available, adopted]
            Details:
              oneOf: [{ $ref: '#/components/schemas/Dog' }, { $ref: '#/components/schemas/Cat' }]
              discriminator: { propertyName: kind, mapping: { dog: '#/components/schemas/Dog', cat: '#/components/schemas/Cat' } }
            Dog:
              type: object
              required: [kind, goodBoy]
              properties: { kind: { type: string }, goodBoy: { type: boolean } }
            Cat:
              type: object
              required: [kind, livesLeft]
              properties: { kind: { type: string }, livesLeft: { type: integer, format: int32, minimum: 0, maximum: 9 } }
        """;

    /// <summary>The same JSON as the schemas, in a given namespace: as the generator writes them, or as a team might by hand.</summary>
    private static string Models(string ns) => $$"""
        namespace {{ns}}
        {
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json.Serialization;

            public sealed record NewPet([property: MinLength(1), MaxLength(50)] string Name, [property: RegularExpression("^[a-z-]+$")] string? Tag);
            public sealed record Pet(long Id, string Name, Status Status, Details Details);

            [JsonConverter(typeof(JsonStringEnumConverter<Status>))]
            public enum Status { [JsonStringEnumMemberName("available")] Available, [JsonStringEnumMemberName("adopted")] Adopted }

            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(Dog), "dog")]
            [JsonDerivedType(typeof(Cat), "cat")]
            public abstract record Details;
            public sealed record Dog(bool GoodBoy) : Details;
            public sealed record Cat([property: Range(0, 9)] int LivesLeft) : Details;
        }
        """;

    private static string PetsController(string ns) => $$"""
        using System.Collections.Generic;
        using Microsoft.AspNetCore.Mvc;
        using {{ns}};

        [ApiController]
        public sealed class PetsController : ControllerBase
        {
            [HttpGet("/pets", Name = "listPets")]
            [ProducesResponseType<IReadOnlyList<Pet>>(200, "application/json")]
            public void List() { }

            [HttpPost("/pets", Name = "addPet")]
            [ProducesResponseType<Pet>(201, "application/json")]
            public void Add(NewPet pet) { }
        }
        """;

    [Test]
    public void Generated_models_are_consistent_by_construction()
    {
        Verify(PetsController("Contract") + Models("Contract"), ModelSpec).Should().BeEmpty();
    }

    [Test]
    public void A_hand_written_model_that_matches_the_schema_is_noted_as_not_consistent_by_construction()
    {
        Verify(PetsController("Mine") + Models("Mine"), ModelSpec).Should().Equal(
            "MM021: PetsController.Add (POST /pets): the 201 response is Pet, which matches the schema Pet today; use the generated types so it stays consistent by construction",
            "MM021: PetsController.Add (POST /pets): the request body is NewPet, which matches the schema NewPet today; use the generated types so it stays consistent by construction",
            "MM021: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which matches the schema Pet[] today; use the generated types so it stays consistent by construction");
    }

    [Test]
    public void Every_difference_between_a_model_and_its_schema_is_reported_at_its_json_path()
    {
        var drifted = """
            namespace Mine
            {
                using System.Text.Json.Serialization;

                public sealed class NewPet
                {
                    public string? Name { get; set; }                         // the schema requires it
                    [JsonPropertyName("label")] public string? Tag { get; set; }  // renamed on the wire
                }

                public sealed record Pet(int Id, string Name, Status Status, Details Details, string Owner);

                public enum Status { Available, Adopted }                    // written as numbers

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
                [JsonDerivedType(typeof(Dog), "dog")]
                [JsonDerivedType(typeof(Fish), "fish")]
                public abstract record Details;
                public sealed record Dog(string GoodBoy) : Details;
                public sealed record Fish(bool Fins) : Details;
            }
            """;

        Verify(PetsController("Mine") + drifted, ModelSpec).Should().Equal(
            "MM022: PetsController.Add (POST /pets): the 201 response is Pet, which differs from the schema Pet: details has a derived type for 'fish', which the schema doesn't",
            "MM022: PetsController.Add (POST /pets): the 201 response is Pet, which differs from the schema Pet: details has no derived type for 'cat' (Cat)",
            "MM022: PetsController.Add (POST /pets): the 201 response is Pet, which differs from the schema Pet: details is discriminated by 'type'; the schema uses 'kind'",
            "MM022: PetsController.Add (POST /pets): the 201 response is Pet, which differs from the schema Pet: details(dog).goodBoy is string (string); the schema says boolean",
            "MM022: PetsController.Add (POST /pets): the 201 response is Pet, which differs from the schema Pet: id is int (integer/int32); the schema says integer/int64",
            "MM022: PetsController.Add (POST /pets): the 201 response is Pet, which differs from the schema Pet: owner (Owner) is not in the schema",
            "MM022: PetsController.Add (POST /pets): the 201 response is Pet, which differs from the schema Pet: status is an enum written as a number; the schema has string values (add [JsonConverter(typeof(JsonStringEnumConverter<Status>))])",
            "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: label (Tag) is not in the schema",
            "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: name (Name) can be null; the schema requires it",
            "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: name (Name) doesn't declare the schema's maxLength 50; add [MaxLength(50)]",
            "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: name (Name) doesn't declare the schema's minLength 1; add [MinLength(1)]",
            "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: tag is missing",
            "MM022: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which differs from the schema Pet[]: [].details has a derived type for 'fish', which the schema doesn't",
            "MM022: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which differs from the schema Pet[]: [].details has no derived type for 'cat' (Cat)",
            "MM022: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which differs from the schema Pet[]: [].details is discriminated by 'type'; the schema uses 'kind'",
            "MM022: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which differs from the schema Pet[]: [].details(dog).goodBoy is string (string); the schema says boolean",
            "MM022: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which differs from the schema Pet[]: [].id is int (integer/int32); the schema says integer/int64",
            "MM022: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which differs from the schema Pet[]: [].owner (Owner) is not in the schema",
            "MM022: PetsController.List (GET /pets): the 200 response is IReadOnlyList<Pet>, which differs from the schema Pet[]: [].status is an enum written as a number; the schema has string values (add [JsonConverter(typeof(JsonStringEnumConverter<Status>))])");
    }

    [Test]
    public void A_minimal_api_body_is_compared_with_its_schema_too()
    {
        Verify("""
            using Microsoft.AspNetCore.Builder;

            public static class Api
            {
                public static void Map(App app)
                {
                    app.MapGet("/pets", () => new Contract.Pet[0]).WithName("listPets");
                    app.MapPost("/pets", (Mine.NewPet pet) => { }).WithName("addPet").Produces<Contract.Pet>(201);
                }
            }

            namespace Mine { public sealed record NewPet([property: System.ComponentModel.DataAnnotations.Length(1, 50)] string Name, long Tag); }
            """ + Models("Contract"), ModelSpec).Should().Equal(
                "MM022: MapPost(\"/pets\") (POST /pets): the request body is NewPet, which differs from the schema NewPet: tag (Tag) can't be null; the schema makes it optional",
                "MM022: MapPost(\"/pets\") (POST /pets): the request body is NewPet, which differs from the schema NewPet: tag (Tag) doesn't declare the schema's pattern ^[a-z-]+$; add [RegularExpression(\"^[a-z-]+$\")]",
                "MM022: MapPost(\"/pets\") (POST /pets): the request body is NewPet, which differs from the schema NewPet: tag is long (integer/int64); the schema says string");
    }

    [Test]
    public void Minimal_API_responses_come_from_typed_results_and_produces_calls()
    {
        // Each style of declaring the same responses passes: a Results<...> return type, a single typed result plus
        // .Produces, and .ProducesProblem on the group.
        Verify("""
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http.HttpResults;

            public static class Api
            {
                public static void Map(App app)
                {
                    var things = app.MapGroup("/things");
                    things.MapGet("/{thingId}", async Task<Ok<string>> (int thingId) => { await Task.Yield(); return new Ok<string>(); })
                        .WithName("getThing").Produces(404);
                    app.MapDelete("/things/{thingId}", NoContent (int thingId) => new NoContent()).WithName("deleteThing");
                }
            }
            """).Should().BeEmpty();

        // A plain value is a 200 of that value; a typed result's own response counts; an undeclared one doesn't.
        Verify("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http.HttpResults;

            public static class Api
            {
                public static void Map(App app)
                {
                    app.MapGet("/things/{thingId}", (int thingId) => 42).WithName("getThing");
                    app.MapDelete("/things/{thingId}", Results<NoContent, ProblemHttpResult> (int thingId) => new NoContent())
                        .WithName("deleteThing").ProducesProblem(409);
                }
            }
            """).Should().Equal(
                "MM012: MapGet(\"/things/{thingId}\") (GET /things/{thingId}): the spec documents a 404 response, but the endpoint declares none; declare it with a typed result or .Produces call",
                "MM013: MapDelete(\"/things/{thingId}\") (DELETE /things/{thingId}): the endpoint declares a 409 response, which the spec does not document",
                "MM022: MapGet(\"/things/{thingId}\") (GET /things/{thingId}): the 200 response is int, which differs from the schema string: the type is int (integer/int32); the schema says string");
    }

    [Test]
    public void Minimal_API_response_models_are_compared_with_their_schemas()
    {
        Verify("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http.HttpResults;

            public static class Api
            {
                public static void Map(App app)
                {
                    app.MapGet("/pets", Ok<Contract.Pet[]> () => new Ok<Contract.Pet[]>()).WithName("listPets");
                    app.MapPost("/pets", Results<Created<Mine.Pet>, ValidationProblem> (Contract.NewPet pet) => new Created<Mine.Pet>()).WithName("addPet");
                }
            }

            namespace Mine { public sealed record Pet(long Id, string Name); }
            """ + Models("Contract"), ModelSpec).Should().Equal(
                "MM013: MapPost(\"/pets\") (POST /pets): the endpoint declares a 400 response, which the spec does not document",
                "MM022: MapPost(\"/pets\") (POST /pets): the 201 response is Pet, which differs from the schema Pet: details is missing",
                "MM022: MapPost(\"/pets\") (POST /pets): the 201 response is Pet, which differs from the schema Pet: status is missing");
    }

    [Test]
    public void Constraints_are_compared_as_the_served_document_would_carry_them()
    {
        var drifted = """
            namespace Mine
            {
                using System.ComponentModel.DataAnnotations;
                using System.Text.Json.Serialization;

                public sealed class NewPet
                {
                    [StringLength(100, MinimumLength = 1)] public string Name { get; set; } = "";   // 100, not 50
                    public string? Tag { get; set; }                                                  // no pattern
                    [Range(1, 5)] public int Age { get; set; }                                        // not in the schema at all
                }
            }
            """;

        Verify("""
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class PetsController : ControllerBase
            {
                [HttpGet("/pets", Name = "listPets")]
                [ProducesResponseType<System.Collections.Generic.IReadOnlyList<Contract.Pet>>(200, "application/json")]
                public void List() { }

                [HttpPost("/pets", Name = "addPet")]
                [ProducesResponseType<Contract.Pet>(201, "application/json")]
                public void Add(Mine.NewPet pet) { }
            }
            """ + drifted + Models("Contract"), ModelSpec).Should().Equal(
                "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: age (Age) is not in the schema",
                "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: name (Name) declares maxLength 100; the schema says 50",
                "MM022: PetsController.Add (POST /pets): the request body is NewPet, which differs from the schema NewPet: tag (Tag) doesn't declare the schema's pattern ^[a-z-]+$; add [RegularExpression(\"^[a-z-]+$\")]");
    }

    [Test]
    public void An_annotation_on_a_record_parameter_is_reported_as_misplaced()
    {
        // On a positional record's parameter, a DataAnnotation applies to the parameter: the property doesn't have it, so
        // ASP.NET Core's OpenAPI document doesn't carry it.
        Verify("""
            using Microsoft.AspNetCore.Builder;
            using System.ComponentModel.DataAnnotations;

            public static class Api
            {
                public static void Map(App app)
                {
                    app.MapGet("/pets", () => new Contract.Pet[0]).WithName("listPets");
                    app.MapPost("/pets", (Mine.NewPet pet) => { }).WithName("addPet").Produces<Contract.Pet>(201);
                }
            }

            namespace Mine { public sealed record NewPet([MaxLength(50)] string Name, [property: RegularExpression("^[a-z-]+$")] string? Tag); }
            """ + Models("Contract"), ModelSpec).Should().Equal(
                "MM022: MapPost(\"/pets\") (POST /pets): the request body is NewPet, which differs from the schema NewPet: name (Name) declares maxLength on the record's parameter, which the OpenAPI document doesn't see; target the property: [property: MaxLength(50)]",
                "MM022: MapPost(\"/pets\") (POST /pets): the request body is NewPet, which differs from the schema NewPet: name (Name) doesn't declare the schema's minLength 1; add [MinLength(1)]");
    }

    [Test]
    public void Parameter_constraints_are_compared_too()
    {
        const string spec = """
            openapi: 3.1.0
            info: { title: T, version: '1' }
            paths:
              /things:
                get:
                  operationId: listThings
                  parameters:
                    - { name: limit, in: query, schema: { type: integer, minimum: 1, maximum: 100 } }
                    - { name: q, in: query, schema: { type: string, maxLength: 20 } }
                  responses:
                    '204': { description: ok }
            """;

        Verify("""
            using System.ComponentModel.DataAnnotations;
            using Microsoft.AspNetCore.Mvc;

            public sealed class ThingsController : ControllerBase
            {
                [HttpGet("/things", Name = "listThings")]
                [ProducesResponseType(204)]
                public void List([Range(1, 100)] int? limit, [MaxLength(20)] string? q) { }
            }
            """, spec).Should().BeEmpty();

        Verify("""
            using System.ComponentModel.DataAnnotations;
            using Microsoft.AspNetCore.Mvc;

            public sealed class ThingsController : ControllerBase
            {
                [HttpGet("/things", Name = "listThings")]
                [ProducesResponseType(204)]
                public void List([Range(0, 1000)] int? limit, [MinLength(2)] string? q) { }
            }
            """, spec).Should().Equal(
                "MM023: ThingsController.List (GET /things): 'limit' declares maximum 1000; the schema says 100",
                "MM023: ThingsController.List (GET /things): 'limit' declares minimum 0; the schema says 1",
                "MM023: ThingsController.List (GET /things): 'q' declares minLength 2, which the schema doesn't have",
                "MM023: ThingsController.List (GET /things): 'q' doesn't declare the schema's maxLength 20; add [MaxLength(20)]");
    }

    [Test]
    public void Nothing_is_verified_unless_the_spec_asks_for_it()
    {
        Verify("public sealed class Empty { }", parts: "Contract").Should().BeEmpty();
    }

    private static List<string> Verify(string code, string spec) => Verify(code, "Verify", spec);

    private static List<string> Verify(string code, string parts = "Verify", string spec = Spec)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(AspNet), CSharpSyntaxTree.ParseText(code)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty("the test code should compile");

        var driver = CSharpGeneratorDriver.Create(
            [new OpenApiSourceGenerator().AsSourceGenerator()],
            [new Text("/specs/t.yaml", spec)],
            optionsProvider: new Options(parts));
        return driver.RunGenerators(compilation).GetRunResult().Diagnostics
            .Where(d => int.TryParse(d.Id.AsSpan(2), out var n) && n is >= 10 and < 30) // the verification diagnostics, MM010 onwards
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options(string parts) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _file = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "T",
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = "Contract",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = parts,
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _file;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
