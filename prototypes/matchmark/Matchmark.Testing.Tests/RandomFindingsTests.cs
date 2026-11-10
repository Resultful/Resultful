using System.Net;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// What random specs run against their twins found (RandomSpecTests), each kept as the smallest spec that shows it:
/// a query parameter named like the validator's own words, and a union whose first member's sample is also another's.
/// </summary>
[TestFixture]
public sealed class RandomFindingsTests
{
    private const string Value = """
        {
          "openapi": "3.1.0",
          "info": { "title": "T", "version": "1" },
          "paths": {
            "/items": {
              "get": {
                "operationId": "listItems",
                "parameters": [ { "name": "value", "in": "query", "schema": { "type": "integer" } } ],
                "responses": { "204": { "description": "none" }, "400": { "description": "invalid" } }
              }
            }
          }
        }
        """;

    [Test]
    public async Task A_query_parameter_named_value_is_named_in_the_twin_s_validation_problem()
    {
        using var client = new HttpClient(new SpecTwin(Value)) { BaseAddress = new Uri("http://twin/") };
        var response = await client.GetAsync(new Uri("items?value=not-a-number", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
        errors.Select(e => e.Key).Should().Equal(["value"], "the parameter is named value; the body isn't involved");
    }

    private const string Union = """
        {
          "openapi": "3.1.0",
          "info": { "title": "T", "version": "1" },
          "paths": {},
          "components": {
            "schemas": {
              "Picked": { "type": "object", "required": ["kind"], "properties": { "kind": { "type": "string", "enum": ["picked"] } } },
              "Named": { "type": "object", "required": ["kind"], "properties": { "kind": { "type": "string", "maxLength": 37 } } },
              "Either": { "oneOf": [ { "$ref": "#/components/schemas/Picked" }, { "$ref": "#/components/schemas/Named" } ] }
            }
          }
        }
        """;

    [Test]
    public void A_union_is_sampled_from_whichever_member_gives_a_valid_value()
    {
        // Random seed 40's shape: Picked's sample, { "kind": "picked" }, is a Named too, so it breaks the oneOf. Named's
        // sample, { "kind": "sample" }, isn't a Picked, so it's the valid value a sampler stuck on the first member missed.
        var spec = OpenApiSpec.Parse(Union);
        var validator = new SchemaValidator(spec);
        var samples = new Samples(spec, validator);
        var either = new JsonObject { ["$ref"] = "#/components/schemas/Either" };

        foreach (var full in new[] { true, false })
        {
            samples.TrySample(either, full, Direction.Response, out var value).Should().BeTrue();
            validator.IsValid(value, either).Should().BeTrue(value?.ToJsonString());
        }
    }
}
