using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// Invariants across a collection (docs/reference/invariants.md) in the twin and the suite, on a small spec of owners
/// and their pets: references to nothing refused, and a delete restricted, cascaded or cleared as each reference says.
/// </summary>
[TestFixture]
public sealed class TraitTests
{
    private static string Spec(string onDelete, bool nullable = false) => $$"""
        {
          "openapi": "3.1.1",
          "info": { "title": "Owners", "version": "1.0.0" },
          "paths": {
            "/owners": {
              "post": {
                "operationId": "createOwner",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Owner" } } } },
                "responses": { "201": { "description": "Created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Owner" } } } } }
              }
            },
            "/owners/{ownerId}": {
              "get": {
                "operationId": "getOwner",
                "parameters": [ { "name": "ownerId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "The owner", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Owner" } } } }, "404": { "description": "None" } }
              },
              "delete": {
                "operationId": "deleteOwner",
                "parameters": [ { "name": "ownerId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "204": { "description": "Deleted" }, "404": { "description": "None" }, "409": { "description": "Still has pets" } }
              }
            },
            "/pets": {
              "post": {
                "operationId": "createPet",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } },
                "responses": {
                  "201": { "description": "Created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } },
                  "422": { "description": "No such owner", "content": { "application/problem+json": { "schema": { "type": "object" } } } }
                }
              }
            },
            "/pets/{petId}": {
              "get": {
                "operationId": "getPet",
                "parameters": [ { "name": "petId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "The pet", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } }, "404": { "description": "None" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Owner": { "type": "object", "required": ["name"], "properties": { "id": { "type": "integer", "readOnly": true }, "name": { "type": "string", "maxLength": 20 } } },
              "Pet": {
                "type": "object",
                "required": ["name", "ownerId"],
                "properties": {
                  "id": { "type": "integer", "readOnly": true },
                  "name": { "type": "string", "maxLength": 20 },
                  "ownerId": { "type": {{(nullable ? "[\"integer\", \"null\"]" : "\"integer\"")}}, "minimum": 1 }
                },
                "x-matchmark-references": { "ownerId": { "target": "/owners/{ownerId}", "onDelete": "{{onDelete}}" } }
              }
            }
          }
        }
        """;

    [Test]
    public async Task A_reference_to_nothing_is_refused_with_the_documented_status()
    {
        using var client = Client(Spec("restrict"));
        var owner = await Created(client, "owners", new { name = "Ada" });

        (await client.PostAsJsonAsync("pets", new { name = "Rex", ownerId = 99 })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync("pets", new { name = "Rex", ownerId = owner })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [TestCase("restrict", HttpStatusCode.Conflict, HttpStatusCode.OK, "1")]
    [TestCase("cascade", HttpStatusCode.NoContent, HttpStatusCode.NotFound, null)]
    [TestCase("clear", HttpStatusCode.NoContent, HttpStatusCode.OK, "null")]
    [TestCase("none", HttpStatusCode.NoContent, HttpStatusCode.OK, "1")]
    public async Task Deleting_what_is_referred_to_does_what_the_reference_says(string onDelete, HttpStatusCode delete, HttpStatusCode petAfter, string? ownerIdAfter)
    {
        using var client = Client(Spec(onDelete, nullable: onDelete == "clear"));
        var owner = await Created(client, "owners", new { name = "Ada" });
        var pet = await Created(client, "pets", new { name = "Rex", ownerId = owner });

        (await client.DeleteAsync($"owners/{owner}")).StatusCode.Should().Be(delete);
        var read = await client.GetAsync($"pets/{pet}");
        read.StatusCode.Should().Be(petAfter);
        if (ownerIdAfter is not null)
        {
            (await read.Content.ReadFromJsonAsync<JsonObject>())!["ownerId"]?.ToJsonString().Should().Be(ownerIdAfter == "null" ? null : ownerIdAfter);
        }
    }

    [Test]
    public async Task The_suite_catches_a_service_that_accepts_a_reference_to_nothing()
    {
        var declared = Spec("restrict");
        var bare = declared.Replace("\"x-matchmark-references\"", "\"x-unused\"", StringComparison.Ordinal);
        var suite = RegressionSuite.FromJson(declared);

        using var keeps = Client(declared);
        await keeps.PostAsJsonAsync("owners", new { name = "Ada" });
        var kept = await suite.RunAsync(keeps);
        kept.Failures.Where(f => f.Check == Checks.ReferenceHolds).Should().BeEmpty(kept.ToString());

        using var forgets = Client(bare);
        await forgets.PostAsJsonAsync("owners", new { name = "Ada" });
        var caught = await suite.RunAsync(forgets);
        caught.Failures.Should().Contain(f => f.Check == Checks.ReferenceHolds && f.Subject.EndsWith("references ownerId to nothing", StringComparison.Ordinal), caught.ToString());
        caught.Failures.Should().Contain(f => f.Check == Checks.ReferenceHolds && f.Subject.EndsWith("references ownerId still referring", StringComparison.Ordinal));
    }

    private static HttpClient Client(string spec) => new(new SpecTwin(spec)) { BaseAddress = new Uri("http://twin/") };

    private static async Task<long> Created(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<long>();
    }
}
