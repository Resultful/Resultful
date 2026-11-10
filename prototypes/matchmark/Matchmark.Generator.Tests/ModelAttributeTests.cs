using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The DataAnnotations on generated models (docs/reference/validation.md, the attribute table), read back by reflection:
/// what ASP.NET Core's OpenAPI document and .NET's validation layer see.
/// </summary>
[TestFixture]
public sealed class ModelAttributeTests
{
    private const string Spec = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Shapes", "version": "1.0.0" },
          "paths": {
            "/shapes": {
              "post": {
                "operationId": "createShape",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Shape" } } } },
                "responses": {
                  "201": { "description": "Created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Shape" } } } },
                  "400": { "description": "Invalid", "content": { "application/problem+json": { "schema": { "$ref": "#/components/schemas/HttpValidationProblemDetails" } } } }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Shape": {
                "type": "object",
                "x-matchmark-rules": [ { "rule": "self.sides != 5", "message": "Pentagons are another API." } ],
                "required": ["name", "sides", "ratio", "tags"],
                "properties": {
                  "name": { "type": "string", "minLength": 1, "maxLength": 20, "pattern": "^[a-z]+$" },
                  "sides": { "type": "integer", "minimum": 3, "maximum": 12 },
                  "ratio": { "type": "number", "exclusiveMinimum": 0, "exclusiveMaximum": 1 },
                  "tags": { "type": "array", "minItems": 1, "maxItems": 4, "items": { "type": "string" } },
                  "kind": { "type": "string", "enum": ["flat", "solid"] },
                  "owner": { "type": "string", "format": "email" },
                  "home": { "type": "string", "format": "uri" },
                  "icon": { "type": "string", "format": "byte" },
                  "step": { "type": "number", "multipleOf": 0.25 },
                  "colours": { "type": "array", "uniqueItems": true, "items": { "type": "string" } },
                  "labels": { "type": "object", "minProperties": 1, "maxProperties": 2, "additionalProperties": { "type": "string" } }
                }
              },
              "HttpValidationProblemDetails": { "type": "object", "properties": { "errors": { "type": "object", "additionalProperties": { "type": "array", "items": { "type": "string" } } } } }
            }
          }
        }
        """;

    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.Extensions.DependencyInjection;
        using Resultful;

        namespace Shapes.Api;

        public sealed class Handlers : IShapesHandlers
        {
            public Task<Result<Shape, CreateShapeError>> CreateShape(Shape body, CancellationToken cancellationToken)
                => Task.FromResult(Result.Ok<Shape, CreateShapeError>(body));
        }

        public static class Server
        {
            public static async Task<WebApplication> StartAsync(bool net)
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddShapes();
                if (net)
                {
                    builder.Services.AddShapesValidation();
                }

                builder.Services.AddSingleton<IShapesHandlers, Handlers>();
                var app = builder.Build();
                app.MapShapes();
                await app.StartAsync();
                return app;
            }
        }
        """;

    private static readonly Assembly Generated = Build();

    private static readonly Type Shape = Generated.GetTypes().Single(t => t.Name == "Shape");

    private static Assembly Build()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shapes.json");
        File.WriteAllText(path, Spec);
        return GeneratedServer.Build("Shapes", path, [], Source, withRules: true).Assembly;
    }

    /// <summary>
    /// Q45: .NET's validation layer answers bad bodies (Add{Api}() registers the resolver), with errors keyed by the spec's
    /// names and Matchmark's own messages, as the generated validator answered them (the keys below are what it gave).
    /// </summary>
    [TestCase("""{"name":"UPPER","sides":2,"ratio":0.5,"tags":["a"]}""", "name,sides")]
    [TestCase("""{"name":"square","sides":4,"ratio":1,"tags":[]}""", "ratio,tags")]
    [TestCase("""{"name":"","sides":13,"ratio":0,"tags":["a","b","c","d","e"],"kind":"liquid"}""", "kind,name,ratio,sides,tags")]
    [TestCase("""{"name":"square","sides":4,"ratio":0.5,"tags":["a"],"owner":"not an address","icon":"%%%"}""", "icon,owner")]
    [TestCase("""{"name":"pentagon","sides":5,"ratio":0.5,"tags":["a"]}""", "$")]
    public async Task Net_s_layer_keys_errors_and_words_them_as_the_spec_s_validator_does(string body, string keys)
    {
        var app = await (Task<WebApplication>)Generated.GetType("Shapes.Api.Server")!.GetMethod("StartAsync")!.Invoke(null, [false])!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
            var response = await client.PostAsync("shapes", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            var errors = (JsonNode.Parse(await response.Content.ReadAsStringAsync())?["errors"] as JsonObject)!;
            ((int)response.StatusCode).Should().Be(400);
            string.Join(",", errors.Select(e => e.Key).Order(StringComparer.Ordinal)).Should().Be(keys);
            errors.SelectMany(e => e.Value!.AsArray()).Select(m => m!.GetValue<string>()).Should().OnlyContain(m => m.StartsWith("Must ", StringComparison.Ordinal) || m.StartsWith("Pentagons", StringComparison.Ordinal));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static T Attribute<T>(string property) where T : Attribute
        => Shape.GetProperty(property)!.GetCustomAttribute<T>() ?? throw new AssertionException($"{property} has no {typeof(T).Name}");

    [Test]
    public void Lengths_counts_and_patterns()
    {
        Attribute<MinLengthAttribute>("Name").Length.Should().Be(1);
        Attribute<MaxLengthAttribute>("Name").Length.Should().Be(20);
        Attribute<RegularExpressionAttribute>("Name").Pattern.Should().Be("^[a-z]+$");
        Attribute<MinLengthAttribute>("Tags").Length.Should().Be(1, "an array's minItems is a count");
        Attribute<MaxLengthAttribute>("Tags").Length.Should().Be(4);
    }

    [Test]
    public void Ranges_inclusive_and_exclusive()
    {
        var sides = Attribute<System.ComponentModel.DataAnnotations.RangeAttribute>("Sides");
        (Convert.ToDouble(sides.Minimum), Convert.ToDouble(sides.Maximum), sides.MinimumIsExclusive).Should().Be((3d, 12d, false));
        var ratio = Attribute<System.ComponentModel.DataAnnotations.RangeAttribute>("Ratio");
        (Convert.ToDouble(ratio.Minimum), Convert.ToDouble(ratio.Maximum), ratio.MinimumIsExclusive, ratio.MaximumIsExclusive).Should().Be((0d, 1d, true, true));
    }

    [Test]
    public void What_dotnet_has_no_attribute_for_comes_from_Matchmark_Validation()
    {
        Attribute<Matchmark.Validation.MultipleOfAttribute>("Step").Step.Should().Be(0.25);
        Attribute<Matchmark.Validation.UniqueItemsAttribute>("Colours").Should().NotBeNull();
        var labels = Attribute<Matchmark.Validation.PropertyCountAttribute>("Labels");
        (labels.Minimum, labels.Maximum).Should().Be((1, 2));

        new Matchmark.Validation.MultipleOfAttribute(0.1).IsValid(0.3m).Should().BeTrue("decimal arithmetic, not doubles");
        new Matchmark.Validation.MultipleOfAttribute(0.25).IsValid(0.3).Should().BeFalse();
        new Matchmark.Validation.UniqueItemsAttribute().IsValid(new[] { "red", "red" }).Should().BeFalse();
        labels.IsValid(new Dictionary<string, string>()).Should().BeFalse();
        labels.IsValid(new Dictionary<string, string> { ["a"] = "x" }).Should().BeTrue();
    }

    /// <summary>MM030: a keyword nothing on .NET's layer checks, reported per property; with Matchmark.Validation, multipleOf is checked.</summary>
    [Test]
    public void A_keyword_no_attribute_checks_is_reported()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shapes-const.json");
        File.WriteAllText(path, Spec.Replace("\"kind\": { \"type\": \"string\", \"enum\": [\"flat\", \"solid\"] }", "\"kind\": { \"type\": \"string\", \"not\": { \"const\": \"liquid\" } }", StringComparison.Ordinal));
        var diagnostics = GeneratedServer.Build("Shapes", path, [], Source, withRules: true).Diagnostics;
        diagnostics.Should().Contain(d => d.Id == "MM030" && d.GetMessage(null).StartsWith("Shape.kind has not", StringComparison.Ordinal));
        diagnostics.Should().NotContain(d => d.Id == "MM030" && d.GetMessage(null).StartsWith("Shape.step", StringComparison.Ordinal), "Matchmark.Validation is referenced, so multipleOf is checked");
    }

    [Test]
    public void A_const_is_the_one_allowed_value()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shapes-only-flat.json");
        File.WriteAllText(path, Spec.Replace("\"kind\": { \"type\": \"string\", \"enum\": [\"flat\", \"solid\"] }", "\"kind\": { \"type\": \"string\", \"const\": \"flat\" }", StringComparison.Ordinal));
        var shape = GeneratedServer.Build("Shapes", path, [], Source, withRules: true).Assembly.GetTypes().Single(t => t.Name == "Shape");
        shape.GetProperty("Kind")!.GetCustomAttribute<AllowedValuesAttribute>()!.Values.Should().Equal(new object?[] { "flat", null });
    }

    [Test]
    public void Enums_and_formats_kept_as_text()
    {
        Attribute<AllowedValuesAttribute>("Kind").Values.Should().Equal("flat", "solid", null);
        Attribute<EmailAddressAttribute>("Owner").Should().NotBeNull();
        Attribute<UrlAttribute>("Home").Should().NotBeNull();
        Attribute<Base64StringAttribute>("Icon").Should().NotBeNull();
    }
}
