using System.ComponentModel.DataAnnotations;
using System.Reflection;
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

    private static readonly Type Shape = Build();

    private static Type Build()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shapes.json");
        File.WriteAllText(path, Spec);
        var (assembly, _) = GeneratedServer.Build("Shapes", path, [], string.Empty, withRules: false);
        return assembly.GetTypes().Single(t => t.Name == "Shape");
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
        (sides.Minimum, sides.Maximum, sides.MinimumIsExclusive).Should().Be((3, 12, false));
        var ratio = Attribute<System.ComponentModel.DataAnnotations.RangeAttribute>("Ratio");
        (Convert.ToDouble(ratio.Minimum), Convert.ToDouble(ratio.Maximum), ratio.MinimumIsExclusive, ratio.MaximumIsExclusive).Should().Be((0d, 1d, true, true));
    }

    [Test]
    public void Enums_and_formats_kept_as_text()
    {
        Attribute<AllowedValuesAttribute>("Kind").Values.Should().Equal("flat", "solid");
        Attribute<EmailAddressAttribute>("Owner").Should().NotBeNull();
        Attribute<UrlAttribute>("Home").Should().NotBeNull();
        Attribute<Base64StringAttribute>("Icon").Should().NotBeNull();
    }
}
