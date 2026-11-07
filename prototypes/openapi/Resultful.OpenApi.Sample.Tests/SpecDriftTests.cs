using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using NUnit.Framework;

namespace Resultful.OpenApi.Sample.Tests;

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
    private static async Task<JsonNode> LoadSpec()
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

    /// <summary>oneOf member schema → its oneOf base schema, from the spec.</summary>
    private readonly Dictionary<string, string> _unionMembers = UnionMembers(spec);

    /// <summary>
    /// How ASP.NET Core 10 represents things differently from the spec. Each is a fact about its OpenAPI document,
    /// not a difference in the API, and each is undone here by name so that nothing else is hidden:
    /// </summary>
    internal static readonly string[] KnownDifferences =
    [
        "ASP.NET Core names a oneOf member '{Base}{Member}' (PetDetailsDog), not by its own schema name",
        "ASP.NET Core writes a discriminated oneOf as anyOf",
        "ASP.NET Core writes the discriminator on each member as a single-value enum, and leaves it out of 'required'",
        "ASP.NET Core leaves 'type: string' off string enums",
    ];

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
            var servedName = ServedName(name);
            if (actual[servedName] is not { } servedSchema)
            {
                _differences.Add($"schema {name}: in the spec, not served");
                continue;
            }

            matched.Add(servedName);
            CompareSchema($"schema {name}", schema!, servedSchema, _unionMembers.TryGetValue(name, out var union) ? Discriminator(union) : null);
        }

        foreach (var extra in actual.Select(s => s.Key).Except(matched))
        {
            _differences.Add($"schema {extra}: served, not in the spec");
        }
    }

    private void CompareSchema(string path, JsonNode e, JsonNode a, string? discriminator)
    {
        if (e["oneOf"] is JsonArray oneOf)
        {
            Same($"{path} oneOf", Refs(oneOf), Refs(a["oneOf"] as JsonArray ?? a["anyOf"] as JsonArray));
            Same($"{path} discriminator", e["discriminator"]?["propertyName"]?.ToString(), a["discriminator"]?["propertyName"]?.ToString());
            Same($"{path} discriminator mapping", Mapping(e), Mapping(a));
            return;
        }

        if (e["enum"] is JsonArray values)
        {
            Same($"{path} enum", Join(values), Join(a["enum"] as JsonArray));
            return;
        }

        Same($"{path} type", Types(e), Types(a) ?? (discriminator is null ? null : Types(e)));
        Same($"{path} required", Join(e["required"] as JsonArray, except: discriminator), Join(a["required"] as JsonArray, except: discriminator));

        var eProps = e["properties"]?.AsObject() ?? [];
        var aProps = a["properties"]?.AsObject() ?? [];
        Same($"{path} properties", Keys(eProps), Keys(aProps));
        foreach (var (name, property) in eProps)
        {
            if (name == discriminator || aProps[name] is not { } servedProperty)
            {
                continue;
            }

            var p = $"{path}.{name}";
            Same($"{p} $ref", Ref(property!), Ref(servedProperty));
            Same($"{p} type", Types(property!), Types(servedProperty));
            Same($"{p} items", Ref(property!["items"]) ?? Types(property["items"]), Ref(servedProperty["items"]) ?? Types(servedProperty["items"]));
            foreach (var keyword in new[] { "format", "minLength", "maxLength", "pattern", "minimum", "maximum" })
            {
                Same($"{p} {keyword}", property[keyword]?.ToJsonString(), servedProperty[keyword]?.ToJsonString());
            }
        }
    }

    private string ServedName(string name)
        => _unionMembers.TryGetValue(name, out var union) ? union + name : name;

    private string? Discriminator(string union) => spec["components"]!["schemas"]![union]!["discriminator"]!["propertyName"]!.GetValue<string>();

    private static Dictionary<string, string> UnionMembers(JsonNode document)
    {
        var members = new Dictionary<string, string>();
        foreach (var (name, schema) in document["components"]?["schemas"]?.AsObject() ?? [])
        {
            foreach (var member in schema!["oneOf"] as JsonArray ?? [])
            {
                members[RefName(member!["$ref"]!.GetValue<string>())] = name;
            }
        }

        return members;
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

    private string? Refs(JsonArray? members)
        => members is null ? null : string.Join(", ", members.Select(m => Unserve(Ref(m)!)).Order());

    private string? Mapping(JsonNode schema)
        => schema["discriminator"]?["mapping"] is JsonObject mapping
            ? string.Join(", ", mapping.Select(m => $"{m.Key}={Unserve(RefName(m.Value!.GetValue<string>()))}").Order())
            : null;

    /// <summary>The spec's name for a served schema name.</summary>
    private string Unserve(string servedName)
        => _unionMembers.FirstOrDefault(m => m.Value + m.Key == servedName).Key ?? servedName;

    private static string? Types(JsonNode? schema)
        => schema?["type"] switch
        {
            null => null,
            JsonArray types => string.Join("|", types.Select(t => t!.GetValue<string>()).Order()),
            var type => type.GetValue<string>(),
        };

    private static string Keys(JsonObject value) => string.Join(", ", value.Select(p => p.Key).Order());

    private static string? Join(JsonArray? values, string? except = null)
        => values is null ? null : string.Join(", ", values.Select(v => v!.ToString()).Where(v => v != except).Order());

    private void Same(string what, string? expected, string? actual)
    {
        if (expected != actual)
        {
            _differences.Add($"{what}: spec has '{expected}', served '{actual}'");
        }
    }
}
