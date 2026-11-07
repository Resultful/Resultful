using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Symbolon.Testing.Tests;

/// <summary>The regression engine on its own: small specs, and a stub in place of a server.</summary>
[TestFixture]
public sealed class EngineTests
{
    private const string Spec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "T", "version": "1" },
          "paths": {
            "/things": {
              "get": {
                "operationId": "listThings",
                "responses": {
                  "200": {
                    "description": "ok",
                    "headers": { "X-Total": { "required": true, "schema": { "type": "integer" } } },
                    "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Thing" } } } }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Thing": {
                "type": "object",
                "required": ["id", "shape"],
                "additionalProperties": false,
                "properties": {
                  "id": { "type": "integer", "format": "int64" },
                  "label": { "type": "string", "nullable": true, "maxLength": 3 },
                  "made": { "type": "string", "format": "date-time" },
                  "shape": { "$ref": "#/components/schemas/Shape" }
                }
              },
              "Shape": {
                "oneOf": [{ "$ref": "#/components/schemas/Circle" }, { "$ref": "#/components/schemas/Square" }],
                "discriminator": { "propertyName": "kind", "mapping": { "circle": "#/components/schemas/Circle", "square": "#/components/schemas/Square" } }
              },
              "Circle": { "type": "object", "required": ["kind", "radius"], "properties": { "kind": { "type": "string" }, "radius": { "type": "number", "exclusiveMinimum": 0 } } },
              "Square": { "type": "object", "required": ["kind", "side"], "properties": { "kind": { "type": "string" }, "side": { "type": "number" } } }
            }
          }
        }
        """;

    private static readonly RegressionSuite Suite = RegressionSuite.FromJson(Spec);

    [Test]
    public void The_validator_reports_every_violation_at_its_path()
    {
        var thing = new JsonObject { ["$ref"] = "#/components/schemas/Thing" };
        var value = JsonNode.Parse("""
            { "id": 9223372036854775808, "label": "long", "made": "yesterday", "extra": 1, "shape": { "kind": "circle", "radius": 0 } }
            """);

        Suite.Validator.Validate(value, thing).Should().BeEquivalentTo(
            "id is 9223372036854775808, which doesn't fit int64",
            "label is 4 characters; the schema's maxLength is 3",
            "made is 'yesterday', which is not a valid date-time",
            "extra is not in the schema, which allows no other properties",
            "shape.radius is 0; it must be greater than 0");
    }

    [Test]
    public void A_discriminated_union_is_checked_against_the_member_it_names()
    {
        var thing = new JsonObject { ["$ref"] = "#/components/schemas/Thing" };

        Suite.Validator.Validate(JsonNode.Parse("""{ "id": 1, "label": null, "shape": { "kind": "square", "side": 2 } }"""), thing).Should().BeEmpty("3.0's nullable is understood");
        Suite.Validator.Validate(JsonNode.Parse("""{ "id": 1, "shape": { "kind": "square", "radius": 2 } }"""), thing)
            .Should().Equal("shape.side is missing; the schema requires it");
        Suite.Validator.Validate(JsonNode.Parse("""{ "id": 1, "shape": { "kind": "hexagon" } }"""), thing)
            .Should().Equal("shape.kind is 'hexagon', which is not one of 'circle', 'square'");
    }

    [Test]
    public void Samples_take_every_member_of_a_union_and_keep_to_exclusive_bounds()
    {
        var thing = new JsonObject { ["$ref"] = "#/components/schemas/Thing" };

        var alternatives = Suite.Samples.Alternatives(thing);

        alternatives.Select(a => a.Label).Should().Equal("", " (square)");
        alternatives.Should().OnlyContain(a => Suite.Validator.IsValid(a.Value, thing));
        alternatives[0].Value!["shape"]!["radius"]!.GetValue<decimal>().Should().BePositive();
        Suite.Samples.Violations(alternatives[0].Value, thing).Select(v => $"{v.Path}: {v.Description}")
            .Should().Contain(["shape.radius: equal to its exclusiveMinimum 0", "label: longer than its maxLength 3", "shape.kind: not one of 'circle', 'square'"]);
    }

    [Test]
    public async Task A_missing_required_header_and_an_extra_property_are_failures()
    {
        using var client = new HttpClient(new Stub("""[{ "id": 1, "shape": { "kind": "square", "side": 1 }, "colour": "red" }]""", header: false))
        {
            BaseAddress = new Uri("http://stub/"),
        };

        var report = await Suite.RunAsync(client);

        report.Failures.Select(f => $"{f.Check}: {f.Message}").Should().BeEquivalentTo(
            "HeadersDocumented: the 200 has no X-Total header, which the spec requires",
            "BodyConforms: the 200 body breaks its schema: [0].colour is not in the schema, which allows no other properties",
            "HeadersDocumented: the 200 has no X-Total header, which the spec requires",
            "BodyConforms: the 200 body breaks its schema: [0].colour is not in the schema, which allows no other properties");
    }

    [Test]
    public async Task A_conforming_answer_passes()
    {
        using var client = new HttpClient(new Stub("""[{ "id": 1, "shape": { "kind": "square", "side": 1 } }]""", header: true))
        {
            BaseAddress = new Uri("http://stub/"),
        };

        (await Suite.RunAsync(client)).Failures.Should().BeEmpty();
    }

    private sealed class Stub(string body, bool header) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (header)
            {
                response.Headers.Add("X-Total", "1");
            }

            return Task.FromResult(response);
        }
    }
}
