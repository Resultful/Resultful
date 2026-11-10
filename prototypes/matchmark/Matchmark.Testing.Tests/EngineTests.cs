using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

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
            .Should().Equal("shape.side is missing; the schema requires it", "shape is 'square' by its kind, but it matches Circle too; a oneOf must match exactly one");
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

    // ---------------------------------------------------------------- what HTTP and the schema promise, on a stub API

    private const string Widgets = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Widgets", "version": "1" },
          "paths": {
            "/widgets": {
              "get": { "operationId": "listWidgets", "responses": { "200": { "description": "ok", "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Widget" } } } } } } },
              "post": {
                "operationId": "createWidget",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Widget" } } } },
                "responses": {
                  "201": { "description": "created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Widget" } } } },
                  "400": { "description": "invalid", "content": { "application/problem+json": { "schema": { "type": "object" } } } }
                }
              }
            },
            "/widgets/{id}": {
              "get": {
                "operationId": "getWidget",
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Widget" } } } },
                  "400": { "description": "invalid", "content": { "application/problem+json": { "schema": { "type": "object" } } } },
                  "404": { "description": "missing" }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Widget": {
                "type": "object",
                "required": ["id", "name", "secret"],
                "properties": {
                  "id": { "type": "integer", "readOnly": true },
                  "name": { "type": "string", "maxLength": 20 },
                  "secret": { "type": "string", "writeOnly": true }
                }
              }
            }
          }
        }
        """;

    [Test]
    public void A_read_only_value_is_left_out_of_valid_requests_and_sent_on_purpose_once()
    {
        var suite = RegressionSuite.FromJson(Widgets);

        suite.Cases.Where(c => c.Kind == CaseKind.Valid && c.Operation.Id == "createWidget").Should()
            .OnlyContain(c => !c.Request.Body!.Contains("\"id\"", StringComparison.Ordinal), "the server sets a read-only id");
        suite.Case("createWidget: valid, with read-only id sent").Request.Body.Should().Contain("\"id\":987654321");
    }

    [TestCase(null, new string[0])]
    [TestCase("stores the read-only id it was sent", new[] { Checks.ReadOnlyIgnored })]
    [TestCase("sends the write-only secret back", new[] { Checks.WriteOnlyHidden })]
    [TestCase("answers a create with a Location that can't be read", new[] { Checks.LocationReadable })]
    [TestCase("changes what it holds when it is read", new[] { Checks.SafeChangesNothing })]
    public async Task Each_promise_is_checked(string? fault, string[] failing)
    {
        using var client = new HttpClient(new WidgetApi(fault)) { BaseAddress = new Uri("http://widgets/") };

        var report = await RegressionSuite.FromJson(Widgets).RunAsync(client);

        var promises = new[] { Checks.ReadOnlyIgnored, Checks.WriteOnlyHidden, Checks.LocationReadable, Checks.SafeChangesNothing };
        report.Failures.Select(f => f.Check).Where(promises.Contains).Distinct().Should().BeEquivalentTo(failing, report.ToString());
    }

    /// <summary>A Widgets API in memory: correct, or with one fault.</summary>
    private sealed class WidgetApi(string? fault) : HttpMessageHandler
    {
        private readonly Dictionary<long, (string Name, string Secret)> _widgets = [];
        private long _next = 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/widgets")
            {
                JsonNode? body;
                try
                {
                    body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken) is { Length: > 0 } t ? t : "null");
                }
                catch (System.Text.Json.JsonException)
                {
                    body = null;
                }

                if (body is not JsonObject w || w["name"] is not JsonValue n || !n.TryGetValue<string>(out var name) || name.Length > 20
                    || w["secret"] is not JsonValue s || !s.TryGetValue<string>(out var secret))
                {
                    return Json(HttpStatusCode.BadRequest, """{"errors":{"body":["Not a widget."]}}""", "application/problem+json");
                }

                var id = fault == "stores the read-only id it was sent" && w["id"] is JsonValue sent && sent.TryGetValue<long>(out var chosen) ? chosen : _next++;
                _widgets[id] = (name, secret);
                var created = Json(HttpStatusCode.Created, Widget(id));
                created.Headers.Location = new Uri(fault == "answers a create with a Location that can't be read" ? "/widgets/424242" : $"/widgets/{id}", UriKind.Relative);
                return created;
            }

            if (request.Method == HttpMethod.Get && path == "/widgets")
            {
                return Json(HttpStatusCode.OK, "[" + string.Join(",", _widgets.Keys.Order().Select(Widget)) + "]");
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/widgets/", StringComparison.Ordinal))
            {
                if (!long.TryParse(path["/widgets/".Length..], out var id))
                {
                    return Json(HttpStatusCode.BadRequest, """{"errors":{"id":["Not a number."]}}""", "application/problem+json");
                }

                if (fault == "changes what it holds when it is read")
                {
                    _widgets[_next] = ("Reader", "s");
                    _next++;
                }

                return _widgets.ContainsKey(id) ? Json(HttpStatusCode.OK, Widget(id)) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string Widget(long id)
            => fault == "sends the write-only secret back"
                ? $$"""{"id":{{id}},"name":"{{_widgets[id].Name}}","secret":"{{_widgets[id].Secret}}"}"""
                : $$"""{"id":{{id}},"name":"{{_widgets[id].Name}}"}""";

        private static HttpResponseMessage Json(HttpStatusCode status, string body, string type = "application/json")
            => new(status) { Content = new StringContent(body, Encoding.UTF8, type) };
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
