using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The scoped trait (docs/reference/invariants.md#scoped): the twin keeps each principal's resources from the others, and
/// the suite, given two principals, catches a service that doesn't; given one, it says the scope went unchecked.
/// </summary>
[TestFixture]
public sealed class ScopedTests
{
    private const string Spec = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Notes", "version": "1.0.0" },
          "security": [ { "bearer": [] } ],
          "paths": {
            "/notes": {
              "post": {
                "operationId": "createNote",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Note" } } } },
                "responses": { "201": { "description": "Created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Note" } } } } }
              },
              "get": {
                "operationId": "listNotes",
                "responses": { "200": { "description": "Mine", "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Note" } } } } } }
              }
            },
            "/notes/{noteId}": {
              "parameters": [ { "name": "noteId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
              "get": { "operationId": "getNote", "responses": { "200": { "description": "The note", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Note" } } } }, "404": { "description": "None" } } },
              "delete": { "operationId": "deleteNote", "responses": { "204": { "description": "Deleted" }, "404": { "description": "None" } } }
            }
          },
          "components": {
            "securitySchemes": { "bearer": { "type": "http", "scheme": "bearer" } },
            "schemas": {
              "Note": {
                "type": "object",
                "required": ["text"],
                "properties": { "id": { "type": "integer", "readOnly": true }, "text": { "type": "string", "maxLength": 40 } },
                "x-matchmark-scoped": { "by": "principal" }
              }
            }
          }
        }
        """;

    private static readonly KeyValuePair<string, string> Ada = new("Authorization", "Bearer ada");
    private static readonly KeyValuePair<string, string> Bo = new("Authorization", "Bearer bo");

    [Test]
    public async Task The_twin_keeps_a_principal_s_resources_from_another()
    {
        using var client = new HttpClient(new SpecTwin(Spec)) { BaseAddress = new Uri("http://twin/") };
        var created = await Send(client, HttpMethod.Post, "notes", Ada, new { text = "mine" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.ToJsonString();

        (await Send(client, HttpMethod.Get, $"notes/{id}", Bo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await Send(client, HttpMethod.Get, "notes", Bo)).Content.ReadFromJsonAsync<JsonArray>()).Should().BeEmpty();
        (await Send(client, HttpMethod.Delete, $"notes/{id}", Bo)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Send(client, HttpMethod.Get, $"notes/{id}", Ada)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await Send(client, HttpMethod.Get, "notes", Ada)).Content.ReadFromJsonAsync<JsonArray>()).Should().HaveCount(1);
    }

    [Test]
    public async Task The_suite_with_two_principals_catches_a_service_that_shares()
    {
        var suite = RegressionSuite.FromJson(Spec);
        var options = new RegressionOptions();
        options.Principals.Add(Ada);
        options.Principals.Add(Bo);

        using var keeps = new HttpClient(new SpecTwin(Spec)) { BaseAddress = new Uri("http://twin/") };
        var kept = await suite.RunAsync(keeps, options);
        kept.Failures.Where(f => f.Check == Checks.ScopeHolds).Should().BeEmpty(kept.ToString());
        kept.Warnings.Where(f => f.Check == Checks.ScopeHolds).Should().BeEmpty(kept.ToString());

        var bare = Spec.Replace("\"x-matchmark-scoped\"", "\"x-unused\"", StringComparison.Ordinal);
        using var shares = new HttpClient(new SpecTwin(bare)) { BaseAddress = new Uri("http://twin/") };
        var caught = await suite.RunAsync(shares, options);
        caught.Failures.Should().Contain(f => f.Check == Checks.ScopeHolds && f.Subject.EndsWith("listed", StringComparison.Ordinal), caught.ToString());
        caught.Failures.Should().Contain(f => f.Check == Checks.ScopeHolds && f.Subject.EndsWith(", GET", StringComparison.Ordinal));
        caught.Failures.Should().Contain(f => f.Check == Checks.ScopeHolds && f.Subject.EndsWith(", DELETE", StringComparison.Ordinal));
    }

    [Test]
    public async Task With_one_principal_the_scope_is_a_gap_not_a_pass()
    {
        var options = new RegressionOptions();
        options.Principals.Add(Ada);
        using var client = new HttpClient(new SpecTwin(Spec)) { BaseAddress = new Uri("http://twin/") };
        var report = await RegressionSuite.FromJson(Spec).RunAsync(client, options);
        report.Warnings.Should().Contain(f => f.Check == Checks.ScopeHolds && f.Message.Contains("unchecked", StringComparison.Ordinal), report.ToString());
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, KeyValuePair<string, string> principal, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation(principal.Key, principal.Value);
        return await client.SendAsync(request);
    }
}
