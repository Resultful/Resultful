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
                "required": ["name", "sides", "ratio", "tags"],
                "properties": {
                  "name": { "type": "string", "minLength": 1, "maxLength": 20, "pattern": "^[a-z]+$" },
                  "sides": { "type": "integer", "minimum": 3, "maximum": 12 },
                  "ratio": { "type": "number", "exclusiveMinimum": 0, "exclusiveMaximum": 1 },
                  "tags": { "type": "array", "minItems": 1, "maxItems": 4, "items": { "type": "string" } },
                  "kind": { "type": "string", "enum": ["flat", "solid"] },
                  "owner": { "type": "string", "format": "email" },
                  "home": { "type": "string", "format": "uri" },
                  "icon": { "type": "string", "format": "byte" }
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
        return GeneratedServer.Build("Shapes", path, [], Source, withRules: false).Assembly;
    }

    /// <summary>
    /// Slice 2 of Q45: the same bad bodies, answered by Matchmark's validator alone and with .NET's layer registered
    /// (Add{Api}Validation, which runs first), give the same status and the same error keys, in the spec's names.
    /// </summary>
    [TestCase("""{"name":"UPPER","sides":2,"ratio":0.5,"tags":["a"]}""")]
    [TestCase("""{"name":"square","sides":4,"ratio":1,"tags":[]}""")]
    [TestCase("""{"name":"","sides":13,"ratio":0,"tags":["a","b","c","d","e"],"kind":"liquid"}""")]
    [TestCase("""{"name":"square","sides":4,"ratio":0.5,"tags":["a"],"owner":"not an address","icon":"%%%"}""")]
    public async Task Net_s_layer_keys_errors_as_the_generated_validator_does(string body)
    {
        async Task<(int Status, string[] Keys, string Text)> Post(bool net)
        {
            var app = await (Task<WebApplication>)Generated.GetType("Shapes.Api.Server")!.GetMethod("StartAsync")!.Invoke(null, [net])!;
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
                var response = await client.PostAsync("shapes", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
                var text = await response.Content.ReadAsStringAsync();
                var keys = (JsonNode.Parse(text)?["errors"] as JsonObject)?.Select(e => e.Key).Order(StringComparer.Ordinal).ToArray() ?? [];
                return ((int)response.StatusCode, keys, text);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }

        var own = await Post(net: false);
        var net = await Post(net: true);
        TestContext.Out.WriteLine($"own: {own.Text}\nnet: {net.Text}");
        own.Status.Should().Be(400);
        net.Status.Should().Be(own.Status);
        net.Keys.Should().Equal(own.Keys);
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
    public void Enums_and_formats_kept_as_text()
    {
        Attribute<AllowedValuesAttribute>("Kind").Values.Should().Equal("flat", "solid", null);
        Attribute<EmailAddressAttribute>("Owner").Should().NotBeNull();
        Attribute<UrlAttribute>("Home").Should().NotBeNull();
        Attribute<Base64StringAttribute>("Icon").Should().NotBeNull();
    }
}
