using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The limit trait (docs/reference/invariants.md#limit): the twin refuses a create past it, per collection, per parent
/// or per principal, and the suite catches a twin of the spec without the declaration.
/// </summary>
[TestFixture]
public sealed class LimitTests
{
    private static string Spec(string scope) => $$"""
        {
          "openapi": "3.1.1",
          "info": { "title": "Boards", "version": "1.0.0" },
          "paths": {
            "/boards/{boardId}/pins": {
              "parameters": [ { "name": "boardId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
              "post": {
                "operationId": "pin",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pin" } } } },
                "responses": {
                  "201": { "description": "Pinned", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pin" } } } },
                  "409": { "description": "The board is full" }
                }
              }
            },
            "/boards/{boardId}/pins/{pinId}": {
              "parameters": [
                { "name": "boardId", "in": "path", "required": true, "schema": { "type": "integer" } },
                { "name": "pinId", "in": "path", "required": true, "schema": { "type": "integer" } }
              ],
              "get": { "operationId": "getPin", "responses": { "200": { "description": "The pin", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pin" } } } }, "404": { "description": "None" } } },
              "delete": { "operationId": "unpin", "responses": { "204": { "description": "Unpinned" }, "404": { "description": "None" } } }
            }
          },
          "components": {
            "schemas": {
              "Pin": {
                "type": "object",
                "required": ["note"],
                "properties": { "id": { "type": "integer", "readOnly": true }, "note": { "type": "string", "maxLength": 40 } },
                "x-matchmark-limit": { "max": 3, "scope": "{{scope}}" }
              }
            }
          }
        }
        """;

    [TestCase("collection", HttpStatusCode.Conflict, HttpStatusCode.Conflict)]
    [TestCase("parent", HttpStatusCode.Conflict, HttpStatusCode.Created)]
    public async Task The_twin_refuses_a_create_past_the_limit_in_its_scope(string scope, HttpStatusCode sameBoard, HttpStatusCode otherBoard)
    {
        using var client = new HttpClient(new SpecTwin(Spec(scope))) { BaseAddress = new Uri("http://twin/") };
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("boards/1/pins", new { note = $"n{i}" })).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        (await client.PostAsJsonAsync("boards/1/pins", new { note = "full" })).StatusCode.Should().Be(sameBoard);
        (await client.PostAsJsonAsync("boards/2/pins", new { note = "other" })).StatusCode.Should().Be(otherBoard);
    }

    [Test]
    public async Task A_principal_limit_counts_each_credential_apart()
    {
        using var client = new HttpClient(new SpecTwin(Spec("principal"))) { BaseAddress = new Uri("http://twin/") };
        async Task<HttpStatusCode> Pin(string token, string note)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "boards/1/pins") { Content = JsonContent.Create(new { note }) };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return (await client.SendAsync(request)).StatusCode;
        }

        for (var i = 0; i < 3; i++)
        {
            (await Pin("ada", $"n{i}")).Should().Be(HttpStatusCode.Created);
        }

        (await Pin("ada", "full")).Should().Be(HttpStatusCode.Conflict);
        (await Pin("bo", "mine")).Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task The_suite_catches_a_service_without_the_limit_and_leaves_room_after()
    {
        var declared = Spec("parent");
        var suite = RegressionSuite.FromJson(declared);

        var keeps = new SpecTwin(declared);
        using var keepsClient = new HttpClient(keeps) { BaseAddress = new Uri("http://twin/") };
        var kept = await suite.RunAsync(keepsClient);
        kept.Failures.Where(f => f.Check == Checks.LimitEnforced).Should().BeEmpty(kept.ToString());
        kept.Failures.Where(f => f.Subject.StartsWith("pin", StringComparison.Ordinal) && f.Check != Checks.LimitEnforced).Should().BeEmpty(kept.ToString());

        var bare = declared.Replace("\"x-matchmark-limit\"", "\"x-unused\"", StringComparison.Ordinal);
        using var forgets = new HttpClient(new SpecTwin(bare)) { BaseAddress = new Uri("http://twin/") };
        var caught = await suite.RunAsync(forgets);
        caught.Failures.Should().Contain(f => f.Check == Checks.LimitEnforced && f.Subject.EndsWith("limit 3 reached", StringComparison.Ordinal), caught.ToString());
    }
}
