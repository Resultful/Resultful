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

    private const string Renames = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Renames", "version": "1.0.0" },
          "paths": {
            "/names": {
              "post": {
                "operationId": "addName",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Name" } } } },
                "responses": { "201": { "description": "Added", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Name" } } } } }
              }
            },
            "/names/{nameId}": {
              "parameters": [ { "name": "nameId", "in": "path", "required": true, "schema": { "type": "integer", "minimum": 1, "maximum": 3 } } ],
              "get": { "operationId": "getName", "responses": { "200": { "description": "It", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Name" } } } }, "404": { "description": "None" } } },
              "put": {
                "operationId": "rename",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Name" } } } },
                "responses": { "200": { "description": "Renamed", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Name" } } } }, "404": { "description": "None" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Name": {
                "type": "object",
                "required": ["text"],
                "properties": { "id": { "type": "integer", "readOnly": true }, "text": { "type": "string", "maxLength": 10 }, "was": { "type": "string", "readOnly": true } },
                "x-matchmark-derived": { "was": { "on": "update", "value": "oldSelf.text" } }
              }
            }
          }
        }
        """;

    [Test]
    public async Task A_field_derived_on_update_is_checked_against_the_resource_before()
    {
        var suite = RegressionSuite.FromJson(Renames);
        using var keeps = new HttpClient(new SpecTwin(Renames)) { BaseAddress = new Uri("http://twin/") };
        await keeps.PostAsJsonAsync("names", new { text = "Ada" });
        var kept = await suite.RunAsync(keeps);
        kept.Failures.Where(f => f.Check == Checks.DerivedHolds).Should().BeEmpty(kept.ToString());

        using var forgets = new HttpClient(new SpecTwin(Renames.Replace("\"x-matchmark-derived\"", "\"x-unused\"", StringComparison.Ordinal))) { BaseAddress = new Uri("http://twin/") };
        await forgets.PostAsJsonAsync("names", new { text = "Ada" });
        var caught = await suite.RunAsync(forgets);
        caught.Failures.Should().Contain(f => f.Check == Checks.DerivedHolds && f.Subject.EndsWith("was derived on update", StringComparison.Ordinal), caught.ToString());
    }

    private const string Pages = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Pages", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "operationId": "listItems",
                "x-matchmark-order": { "by": ["id"] },
                "parameters": [
                  { "name": "limit", "in": "query", "schema": { "type": "integer", "minimum": 1, "maximum": 10 } },
                  { "name": "cursor", "in": "query", "schema": { "type": "string" } },
                  { "name": "sort", "in": "query", "schema": { "type": "string", "enum": ["name", "-name"] } }
                ],
                "responses": { "200": { "description": "A page", "content": { "application/json": { "schema": {
                  "type": "object", "required": ["items"],
                  "properties": { "items": { "type": "array", "items": { "$ref": "#/components/schemas/Item" } }, "next": { "type": ["string", "null"] } } } } } } }
              }
            },
            "/items/{itemId}": {
              "get": {
                "operationId": "getItem",
                "parameters": [ { "name": "itemId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "It", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Item" } } } }, "404": { "description": "None" } }
              }
            }
          },
          "components": { "schemas": { "Item": { "type": "object", "required": ["id", "name"], "properties": { "id": { "type": "integer" }, "name": { "type": "string" } } } } }
        }
        """;

    private static readonly JsonObject[] Stock = [new() { ["id"] = 1, ["name"] = "delta" }, new() { ["id"] = 2, ["name"] = "alpha" }, new() { ["id"] = 3, ["name"] = "charlie" }, new() { ["id"] = 4, ["name"] = "bravo" }];

    /// <summary>A service that pages in id order, but whose second page starts over: each page sorted, the pages not.</summary>
    private static TwinAnswer Paged(TwinCall call, bool keepsOrder)
    {
        var page = call.Query.FirstOrDefault(q => q.Key == "cursor").Value == "2" ? 1 : 0;
        var order = keepsOrder ? Stock : [Stock[2], Stock[3], Stock[0], Stock[1]];
        var items = new JsonArray(order.Skip(page * 2).Take(2).Select(i => (JsonNode?)i.DeepClone()).ToArray());
        return new TwinAnswer(200, new JsonObject { ["items"] = items, ["next"] = page == 0 ? "2" : null });
    }

    [Test]
    public async Task The_order_holds_across_pages()
    {
        var suite = RegressionSuite.FromJson(Pages);
        async Task<RegressionReport> Run(bool keepsOrder)
        {
            var twin = new SpecTwin(Pages);
            twin.On("listItems", call => call.Query.Any(q => q.Key == "sort") ? null : Paged(call, keepsOrder));
            using var client = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
            return await suite.RunAsync(client);
        }

        (await Run(keepsOrder: true)).Failures.Where(f => f.Check == Checks.ListOrdered).Should().BeEmpty();
        var caught = await Run(keepsOrder: false);
        caught.Failures.Should().Contain(f => f.Check == Checks.ListOrdered && f.Subject.EndsWith("across pages", StringComparison.Ordinal), caught.ToString());
    }

    [Test]
    public async Task A_sort_parameter_orders_the_twin_s_list_and_the_suite_checks_it()
    {
        var twin = new SpecTwin(Pages);
        twin.Seed("/items", Stock);
        using var client = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
        var sorted = (await client.GetFromJsonAsync<JsonObject>("items?sort=-name"))!["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>());
        sorted.Should().Equal("delta", "charlie", "bravo", "alpha");

        // A service that ignores the sort parameter, listing in id order whatever it's asked.
        var ignores = new SpecTwin(Pages);
        ignores.Seed("/items", Stock);
        ignores.On("listItems", call => new TwinAnswer(200, new JsonObject { ["items"] = new JsonArray(Stock.Select(i => (JsonNode?)i.DeepClone()).ToArray()), ["next"] = null }));
        using var ignoring = new HttpClient(ignores) { BaseAddress = new Uri("http://twin/") };
        var caught = await RegressionSuite.FromJson(Pages).RunAsync(ignoring);
        caught.Failures.Should().Contain(f => f.Check == Checks.ListOrdered && f.Reproduce.Contains("sort=", StringComparison.Ordinal), caught.ToString());
    }

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
