using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The derived and order traits (docs/reference/invariants.md): the twin computes a derived field and sorts lists, and
/// the suite catches a twin of the spec without the declarations.
/// </summary>
[TestFixture]
public sealed class DerivedAndOrderTests
{
    private const string Spec = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Accounts", "version": "1.0.0" },
          "paths": {
            "/accounts": {
              "post": {
                "operationId": "openAccount",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/NewAccount" } } } },
                "responses": { "201": { "description": "Opened", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Account" } } } } }
              },
              "get": {
                "operationId": "listAccounts",
                "x-matchmark-order": { "by": ["owner desc", "id"] },
                "responses": { "200": { "description": "Accounts", "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Account" } } } } } }
              }
            },
            "/accounts/{accountId}": {
              "get": {
                "operationId": "getAccount",
                "parameters": [ { "name": "accountId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "The account", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Account" } } } }, "404": { "description": "None" } }
              }
            }
          },
          "components": {
            "schemas": {
              "NewAccount": { "type": "object", "required": ["owner", "opening"], "properties": { "owner": { "type": "string", "minLength": 1, "maxLength": 20 }, "opening": { "type": "integer", "minimum": 0, "maximum": 1000 } } },
              "Account": {
                "type": "object",
                "required": ["id", "owner", "balance"],
                "properties": { "id": { "type": "integer", "readOnly": true }, "owner": { "type": "string" }, "balance": { "type": "integer", "readOnly": true } },
                "x-matchmark-derived": { "balance": { "on": "create", "value": "request.opening * 2" } }
              }
            }
          }
        }
        """;

    [Test]
    public async Task The_twin_computes_derived_fields_and_sorts_lists()
    {
        using var client = new HttpClient(new SpecTwin(Spec)) { BaseAddress = new Uri("http://twin/") };

        var opened = await client.PostAsJsonAsync("accounts", new { owner = "Ada", opening = 21 });
        opened.StatusCode.Should().Be(HttpStatusCode.Created);
        (await opened.Content.ReadFromJsonAsync<JsonObject>())!["balance"]!.GetValue<int>().Should().Be(42);
        await client.PostAsJsonAsync("accounts", new { owner = "Bo", opening = 1 });
        await client.PostAsJsonAsync("accounts", new { owner = "Bo", opening = 2 });

        var listed = (await client.GetFromJsonAsync<JsonArray>("accounts"))!.Select(a => $"{a!["owner"]} {a["id"]}");
        listed.Should().Equal("Bo 2", "Bo 3", "Ada 1");
    }

    [Test]
    public async Task The_suite_catches_a_service_that_forgets_them()
    {
        var suite = RegressionSuite.FromJson(Spec);
        using var keeps = new HttpClient(new SpecTwin(Spec)) { BaseAddress = new Uri("http://twin/") };
        var kept = await suite.RunAsync(keeps);
        kept.Failures.Where(f => f.Check is Checks.DerivedHolds or Checks.ListOrdered).Should().BeEmpty(kept.ToString());

        var bare = Spec.Replace("\"x-matchmark-derived\"", "\"x-unused\"", StringComparison.Ordinal).Replace("\"x-matchmark-order\"", "\"x-unused-order\"", StringComparison.Ordinal);
        using var forgets = new HttpClient(new SpecTwin(bare)) { BaseAddress = new Uri("http://twin/") };
        await forgets.PostAsJsonAsync("accounts", new { owner = "Ada", opening = 1 });
        await forgets.PostAsJsonAsync("accounts", new { owner = "Bo", opening = 1 });
        var caught = await suite.RunAsync(forgets);
        caught.Failures.Should().Contain(f => f.Check == Checks.DerivedHolds, caught.ToString());
        caught.Failures.Should().Contain(f => f.Check == Checks.ListOrdered, caught.ToString());
    }
}
