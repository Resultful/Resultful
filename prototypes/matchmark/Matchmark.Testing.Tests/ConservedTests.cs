using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The conserved trait (docs/reference/invariants.md#conserved): the twin checks owned code keeps a total, and the suite
/// catches a service whose transfers make or lose money.
/// </summary>
[TestFixture]
public sealed class ConservedTests
{
    private const string Spec = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Wallets", "version": "1.0.0" },
          "paths": {
            "/wallets": {
              "post": {
                "operationId": "openWallet",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Wallet" } } } },
                "responses": { "201": { "description": "Opened", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Wallet" } } } } }
              }
            },
            "/wallets/{walletId}": {
              "get": {
                "operationId": "getWallet",
                "parameters": [ { "name": "walletId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "The wallet", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Wallet" } } } }, "404": { "description": "None" } }
              }
            },
            "/moves": {
              "post": {
                "operationId": "move",
                "requestBody": { "required": true, "content": { "application/json": {
                  "schema": { "$ref": "#/components/schemas/Move" },
                  "example": { "from": 1, "to": 2, "amount": "5.00" } } } },
                "responses": { "201": { "description": "Moved", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Move" } } } } }
              }
            }
          },
          "components": {
            "schemas": {
              "Wallet": {
                "type": "object",
                "required": ["balance"],
                "properties": { "id": { "type": "integer", "readOnly": true }, "balance": { "type": "string", "pattern": "^[0-9]+\\.[0-9]{2}$", "maxLength": 12 } },
                "x-matchmark-conserved": [ { "total": "balance", "except": ["openWallet"] } ]
              },
              "Move": {
                "type": "object",
                "required": ["from", "to", "amount"],
                "properties": {
                  "id": { "type": "integer", "readOnly": true },
                  "from": { "type": "integer", "minimum": 1, "maximum": 2 },
                  "to": { "type": "integer", "minimum": 1, "maximum": 2 },
                  "amount": { "type": "string", "enum": ["5.00"] }
                },
                "x-matchmark-references": { "from": { "target": "/wallets/{walletId}" }, "to": { "target": "/wallets/{walletId}" } }
              }
            }
          }
        }
        """;

    [TestCase(true, HttpStatusCode.Created)]
    [TestCase(false, HttpStatusCode.InternalServerError)]
    public async Task The_twin_answers_500_when_owned_code_breaks_the_total(bool credits, HttpStatusCode expected)
    {
        var twin = new SpecTwin(Spec);
        twin.On("move", call => Move(call, credits));
        using var client = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
        await client.PostAsJsonAsync("wallets", new { balance = "50.00" });
        await client.PostAsJsonAsync("wallets", new { balance = "0.00" });

        var moved = await client.PostAsJsonAsync("moves", new { from = 1, to = 2, amount = "5.00" });
        moved.StatusCode.Should().Be(expected, await moved.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task The_suite_catches_a_service_that_makes_or_loses_money()
    {
        var suite = RegressionSuite.FromJson(Spec);
        var bare = Spec.Replace("\"x-matchmark-conserved\"", "\"x-unused\"", StringComparison.Ordinal);

        async Task<RegressionReport> Run(bool credits)
        {
            // A service the spec doesn't constrain, so the suite alone has to notice.
            var service = new SpecTwin(bare);
            service.On("move", call => Move(call, credits));
            using var client = new HttpClient(service) { BaseAddress = new Uri("http://twin/") };
            await client.PostAsJsonAsync("wallets", new { balance = "50.00" });
            await client.PostAsJsonAsync("wallets", new { balance = "50.00" });
            return await suite.RunAsync(client);
        }

        var kept = await Run(credits: true);
        kept.Failures.Where(f => f.Check == Checks.TotalConserved).Should().BeEmpty(kept.ToString());
        var caught = await Run(credits: false);
        caught.Failures.Should().Contain(f => f.Check == Checks.TotalConserved && f.Subject.EndsWith("the total of Wallet.balance conserved", StringComparison.Ordinal), caught.ToString());
    }

    /// <summary>Owned code for a move: debits the source, and credits the target unless told not to.</summary>
    private static TwinAnswer? Move(TwinCall call, bool credits)
    {
        var wallets = call.Resources.GetValueOrDefault("/wallets") ?? [];
        var from = wallets.FirstOrDefault(w => w["id"]?.ToJsonString() == call.Body?["from"]?.ToJsonString());
        var to = wallets.FirstOrDefault(w => w["id"]?.ToJsonString() == call.Body?["to"]?.ToJsonString());
        if (from is null || to is null)
        {
            return null;
        }

        var amount = decimal.Parse(call.Body!["amount"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        from["balance"] = (decimal.Parse(from["balance"]!.GetValue<string>(), CultureInfo.InvariantCulture) - amount).ToString("0.00", CultureInfo.InvariantCulture);
        if (credits)
        {
            to["balance"] = (decimal.Parse(to["balance"]!.GetValue<string>(), CultureInfo.InvariantCulture) + amount).ToString("0.00", CultureInfo.InvariantCulture);
        }

        return new TwinAnswer(201, call.Body!.DeepClone());
    }
}
