using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using NUnit.Framework;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The round trip: the spec went in, the generator wrote the server, and ASP.NET Core describes that server in its own
/// OpenAPI document. The two must describe the same API. This catches the generator dropping or misreading anything,
/// and catches hand-written code adding endpoints the spec does not have.
/// </summary>
[TestFixture]
public sealed class SpecDriftTests
{
    [Test]
    public async Task The_running_app_describes_the_same_api_as_the_spec()
    {
        var spec = await LoadSpec();
        JsonNode served;
        await using (var factory = new PetStoreApp())
        {
            using var client = factory.CreateClient();
            served = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        }

        var differences = new SpecComparer(spec, served).Compare();

        // One line per difference, so a failure lists all of them.
        string.Join("\n", differences).Should().BeEmpty();
    }

    /// <summary>Reads the YAML spec and writes it as OpenAPI 3.1 JSON, so both sides are compared in one form.</summary>
    internal static async Task<JsonNode> LoadSpec()
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();
        var yaml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "petstore.yaml"));
        var read = OpenApiDocument.Parse(yaml, "yaml", settings);
        read.Diagnostic!.Errors.Should().BeEmpty();

        using var writer = new StringWriter();
        read.Document!.SerializeAsV31(new OpenApiJsonWriter(writer));
        return JsonNode.Parse(writer.ToString())!;
    }
}

/// <summary>
/// Compares two OpenAPI 3.1 documents: operations, parameters, bodies, responses and component schemas, down to
/// property types and constraints. Titles, descriptions and examples are not compared.
/// </summary>
internal sealed class SpecComparer(JsonNode spec, JsonNode served)
{
    private readonly List<string> _differences = [];

    public List<string> Compare()
    {
        CompareOperations();
        CompareSchemas();
        return _differences;
    }

    private void CompareOperations()
    {
        var expected = Operations(spec);
        var actual = Operations(served);

        foreach (var missing in expected.Keys.Except(actual.Keys))
        {
            _differences.Add($"{missing}: in the spec, not served");
        }

        foreach (var extra in actual.Keys.Except(expected.Keys))
        {
            _differences.Add($"{extra}: served, not in the spec");
        }

        foreach (var key in expected.Keys.Intersect(actual.Keys))
        {
            var e = expected[key];
            var a = actual[key];
            Same($"{key} operationId", e["operationId"]?.GetValue<string>(), a["operationId"]?.GetValue<string>());
            Same($"{key} parameters", Parameters(e), Parameters(a));
            Same($"{key} request body", Content(e["requestBody"]?["content"]), Content(a["requestBody"]?["content"]));

            var eResponses = e["responses"]!.AsObject();
            var aResponses = a["responses"]!.AsObject();
            Same($"{key} responses", Keys(eResponses), Keys(aResponses));
            foreach (var status in eResponses.Select(r => r.Key).Intersect(aResponses.Select(r => r.Key)))
            {
                Same($"{key} {status} body", Content(eResponses[status]!["content"]), Content(aResponses[status]!["content"]));
            }
        }
    }

    private void CompareSchemas()
    {
        var expected = spec["components"]?["schemas"]?.AsObject() ?? [];
        var actual = served["components"]?["schemas"]?.AsObject() ?? [];
        var matched = new HashSet<string>();

        foreach (var (name, schema) in expected)
        {
            if (actual[name] is not { } servedSchema)
            {
                _differences.Add($"schema {name}: in the spec, not served");
                continue;
            }

            matched.Add(name);
            CompareSchema($"schema {name}", schema!, servedSchema);
        }

        foreach (var extra in actual.Select(s => s.Key).Except(matched))
        {
            _differences.Add($"schema {extra}: served, not in the spec");
        }
    }

    /// <summary>Every schema is compared the same way; nothing about ASP.NET Core's own representation is excused.</summary>
    private void CompareSchema(string path, JsonNode e, JsonNode a)
    {
        Same($"{path} oneOf", Refs(e["oneOf"] as JsonArray), Refs(a["oneOf"] as JsonArray));
        Same($"{path} anyOf", Refs(e["anyOf"] as JsonArray), Refs(a["anyOf"] as JsonArray));
        Same($"{path} discriminator", e["discriminator"]?["propertyName"]?.ToString(), a["discriminator"]?["propertyName"]?.ToString());
        Same($"{path} discriminator mapping", Mapping(e), Mapping(a));
        Same($"{path} enum", Join(e["enum"] as JsonArray), Join(a["enum"] as JsonArray));
        Same($"{path} type", Types(e), Types(a));
        Same($"{path} required", Join(e["required"] as JsonArray), Join(a["required"] as JsonArray));

        var eProps = e["properties"]?.AsObject() ?? [];
        var aProps = a["properties"]?.AsObject() ?? [];
        Same($"{path} properties", Keys(eProps), Keys(aProps));
        foreach (var (name, property) in eProps)
        {
            if (aProps[name] is not { } servedProperty)
            {
                continue;
            }

            var p = $"{path}.{name}";
            Same($"{p} $ref", Ref(property!), Ref(servedProperty));
            Same($"{p} type", Types(property!), Types(servedProperty));
            Same($"{p} items", Ref(property!["items"]) ?? Types(property["items"]), Ref(servedProperty["items"]) ?? Types(servedProperty["items"]));
            Same($"{p} enum", Join(property["enum"] as JsonArray), Join(servedProperty["enum"] as JsonArray));
            foreach (var keyword in new[] { "format", "minLength", "maxLength", "pattern", "minimum", "maximum" })
            {
                Same($"{p} {keyword}", property[keyword]?.ToJsonString(), servedProperty[keyword]?.ToJsonString());
            }
        }
    }


    private static Dictionary<string, JsonNode> Operations(JsonNode document)
    {
        var operations = new Dictionary<string, JsonNode>();
        foreach (var (path, item) in document["paths"]!.AsObject())
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                operations[$"{method.ToUpperInvariant()} {path}"] = operation!;
            }
        }

        return operations;
    }

    private static string Parameters(JsonNode operation)
        => string.Join(", ", (operation["parameters"] as JsonArray ?? [])
            .Select(p => $"{p!["in"]}:{p["name"]}{(p["required"]?.GetValue<bool>() == true ? "!" : "")}={Ref(p["schema"]) ?? Types(p["schema"])}/{p["schema"]?["format"]}")
            .Order());

    /// <summary>"application/json → Pet[]" for every media type, so content types and schemas are both compared.</summary>
    private string? Content(JsonNode? content)
        => content is null ? null : string.Join(", ", content.AsObject()
            .Select(c => $"{c.Key} → {SchemaName(c.Value?["schema"])}")
            .Order());

    private string? SchemaName(JsonNode? schema)
        => schema is null ? null
            : Ref(schema) is { } name ? name
            : schema["items"] is { } items ? SchemaName(items) + "[]"
            : Types(schema);

    private static string? Ref(JsonNode? schema)
        => schema?["$ref"]?.GetValue<string>() is { } reference ? RefName(reference) : null;

    private static string RefName(string reference) => reference[(reference.LastIndexOf('/') + 1)..];

    private static string? Refs(JsonArray? members)
        => members is null ? null : string.Join(", ", members.Select(m => Ref(m) ?? Types(m)).Order());

    private static string? Mapping(JsonNode schema)
        => schema["discriminator"]?["mapping"] is JsonObject mapping
            ? string.Join(", ", mapping.Select(m => $"{m.Key}={RefName(m.Value!.GetValue<string>())}").Order())
            : null;

    private static string? Types(JsonNode? schema)
        => schema?["type"] switch
        {
            null => null,
            JsonArray types => string.Join("|", types.Select(t => t!.GetValue<string>()).Order()),
            var type => type.GetValue<string>(),
        };

    private static string Keys(JsonObject value) => string.Join(", ", value.Select(p => p.Key).Order());

    private static string? Join(JsonArray? values)
        => values is null ? null : string.Join(", ", values.Select(v => v!.ToString()).Order());

    private void Same(string what, string? expected, string? actual)
    {
        if (expected != actual)
        {
            _differences.Add($"{what}: spec has '{expected}', served '{actual}'");
        }
    }
}
