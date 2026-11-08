using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// Random valid requests on top of the deterministic cases. The API here fails only for a quantity in the 50s: the
/// deterministic cases (the bounds and a sample) never send one, random ones do, and the failure is shrunk to the
/// smallest request that still fails.
/// </summary>
[TestFixture]
public sealed class RandomTests
{
    private const string Spec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Orders", "version": "1" },
          "paths": {
            "/orders": {
              "post": {
                "operationId": "createOrder",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/NewOrder" } } } },
                "responses": {
                  "201": { "description": "created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/NewOrder" } } } },
                  "400": { "description": "invalid", "content": { "application/problem+json": { "schema": { "type": "object" } } } }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "NewOrder": {
                "type": "object",
                "required": ["name", "quantity"],
                "properties": {
                  "name": { "type": "string", "minLength": 1, "maxLength": 20 },
                  "quantity": { "type": "integer", "minimum": 1, "maximum": 99 },
                  "note": { "type": "string", "maxLength": 50 }
                }
              }
            }
          }
        }
        """;

    [Test]
    public async Task The_deterministic_cases_miss_the_bug()
        => (await Run(new RegressionOptions { Workflows = false })).Failures.Should().BeEmpty();

    [Test]
    public async Task Random_requests_find_it_and_shrink_it_to_the_smallest_that_fails()
    {
        var report = await Run(new RegressionOptions { Workflows = false, Random = 60, Seed = 7 });

        var failure = report.Cases.Where(c => c.Findings.Any(f => f.Check == Checks.NotAServerError)).Select(c => c.Case).First();
        failure.Name.Should().MatchRegex(@"^createOrder: random \d+ \(seed 7\), shrunk in \d+ steps$");
        var body = JsonNode.Parse(failure.Request.Body!)!.AsObject();
        body.Select(p => p.Key).Should().BeEquivalentTo("name", "quantity");
        body["name"]!.GetValue<string>().Should().HaveLength(1);
        body["quantity"]!.GetValue<long>().Should().Be(51, "the smallest quantity that still fails");
    }

    [Test]
    public async Task The_same_seed_draws_the_same_requests()
    {
        static async Task<List<string?>> Bodies(int seed)
            => (await Run(new RegressionOptions { Workflows = false, Random = 5, Seed = seed })).Cases
                .Where(c => c.Case.Name.Contains("random", StringComparison.Ordinal)).Select(c => c.Case.Request.Body).ToList();

        (await Bodies(3)).Should().Equal(await Bodies(3)).And.NotEqual(await Bodies(4));
    }

    private static async Task<RegressionReport> Run(RegressionOptions options)
    {
        using var client = new HttpClient(new Api()) { BaseAddress = new Uri("http://orders/") };
        return await RegressionSuite.FromJson(Spec).RunAsync(client, options);
    }

    /// <summary>Validates as the spec says, and fails with a 500 for a quantity from 51 to 59.</summary>
    private sealed class Api : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            JsonObject? order = null;
            try
            {
                order = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
            }

            if (request.Content!.Headers.ContentType?.MediaType != "application/json" || order is null
                || order["name"] is not JsonValue name || name.GetValueKind() != System.Text.Json.JsonValueKind.String || name.GetValue<string>().Length is < 1 or > 20
                || order["quantity"] is not JsonValue q || q.GetValueKind() != System.Text.Json.JsonValueKind.Number || !long.TryParse(q.ToJsonString(), out var quantity) || quantity is < 1 or > 99
                || order.ContainsKey("note") && (order["note"] is not JsonValue note || note.GetValueKind() != System.Text.Json.JsonValueKind.String || note.GetValue<string>().Length > 50))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"title":"invalid"}""", Encoding.UTF8, "application/problem+json") };
            }

            return quantity is > 50 and < 60
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }
                : new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(order.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
