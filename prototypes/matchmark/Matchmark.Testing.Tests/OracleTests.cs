using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Generator;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The independent oracle. Matchmark's generator, engine and twin read schemas the same way, so a misreading they share
/// passes every other test. Here every sample's component schemas are given to a JSON Schema 2020-12 validator written
/// apart from Matchmark (Corvus.Json.Validator), with values from Matchmark's own <see cref="Samples"/>: a sample, the
/// boundaries (valid) and the violations (invalid). Matchmark's <see cref="SchemaValidator"/> and the oracle must agree
/// on every value, and each sample must be valid and each violation invalid by the oracle too.
/// </summary>
[TestFixture]
public sealed class OracleTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(ThisDirectory(), ".."));

    /// <summary>Each sample's spec but the split one, which is in several files: the CLI bundles it, and the generator's tests read the bundle.</summary>
    public static IEnumerable<string> Specs()
        => Directory.GetDirectories(Path.Combine(Root, "samples")).Where(d => File.Exists(Path.Combine(d, "openapi.yaml")))
            .Select(Path.GetFileName).OfType<string>().Where(s => s != "split").Order(StringComparer.Ordinal).Append("petstore");

    /// <summary>GitHub's and Stripe's specs, when they've been fetched (MATCHMARK_LARGE_SPECS, as for LargeSpecTests).</summary>
    public static IEnumerable<string> LargeSpecs()
        => Environment.GetEnvironmentVariable("MATCHMARK_LARGE_SPECS") is { Length: > 0 } folder && Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal)
            : [];

    [TestCaseSource(nameof(LargeSpecs))]
    public void Matchmark_and_an_independent_validator_agree_on_a_large_spec(string path)
        => Agree(path, Path.GetFileNameWithoutExtension(path), corvus: false); // Corvus's code generation can't take them (testing.md)

    [TestCaseSource(nameof(Specs))]
    public void Matchmark_and_an_independent_validator_agree(string sample)
        => Agree(sample == "petstore" ? Path.Combine(Root, "Matchmark.Sample.Contract", "petstore.yaml") : Path.Combine(Root, "samples", sample, "openapi.yaml"), sample, corvus: true);

    /// <summary>
    /// Matchmark's verdict on each value against the oracles': Corvus.Json.Validator, which compiles the schema to code,
    /// and JsonSchema.Net 7.3.3 (MIT, the last release before its maintenance-fee licence), which interprets it.
    /// </summary>
    private static void Agree(string path, string sample, bool corvus)
    {
        var json = SpecJson.Normalize(File.ReadAllText(path), path, out _);
        var spec = OpenApiSpec.Parse(json);
        var validator = new SchemaValidator(spec);
        var samples = new Samples(spec, validator);
        var schemas = spec.Schemas.ToList();
        if (schemas.Count == 0)
        {
            Assert.Ignore("no component schemas");
        }

        var combined = Combined(schemas);
        var oracles = new List<(string Name, Func<string, JsonNode?, bool> IsValid)>();
        if (corvus)
        {
            var compiled = Corvus.Json.Validator.JsonSchema.FromText(combined, $"https://matchmark.test/{sample}.json");
            oracles.Add(("Corvus", (name, value) =>
            {
                using var document = JsonDocument.Parse(new JsonObject { [name] = value?.DeepClone() }.ToJsonString());
                return compiled.Validate(document.RootElement).IsValid;
            }));
        }

        OpenApiFormats();
        var interpreted = Json.Schema.JsonSchema.FromText(combined);
        var options = new Json.Schema.EvaluationOptions { RequireFormatValidation = true, OutputFormat = Json.Schema.OutputFormat.Flag };
        oracles.Add(("JsonSchema.Net", (name, value) => interpreted.Evaluate(new JsonObject { [name] = value?.DeepClone() }, options).IsValid));
        var disagreements = new List<string>();
        var examined = 0;
        foreach (var (name, _) in schemas)
        {
            var reference = new JsonObject { ["$ref"] = $"#/components/schemas/{name}" };
            var values = new List<(string Label, JsonNode? Value, bool? Expected)>();
            foreach (var full in new[] { true, false })
            {
                if (samples.TrySample(reference, full, Direction.Any, out var value))
                {
                    values.Add((full ? "full sample" : "minimal sample", value, true));
                }
            }

            var root = values.FirstOrDefault().Value;
            if (root is not null)
            {
                values.AddRange(samples.Boundaries(root, reference).Select(v => ($"boundary {v.Description}", v.Value, (bool?)null)));
                values.AddRange(samples.Violations(root, reference).Select(v => ($"violation {v.Description}", v.Value, (bool?)false)));
            }

            foreach (var (label, value, expected) in values)
            {
                examined++;
                var ours = validator.IsValid(value, reference);
                foreach (var (oracle, isValid) in oracles)
                {
                    var theirs = isValid(name, value);
                    if (ours != theirs || (expected is { } e && theirs != e))
                    {
                        disagreements.Add($"{name}: {label}: Matchmark {(ours ? "valid" : "invalid")}, {oracle} {(theirs ? "valid" : "invalid")}: {Short(value)}");
                    }
                }
            }
        }

        TestContext.Out.WriteLine($"{sample}: {schemas.Count} schemas, {examined} values, {string.Join(" and ", oracles.Select(o => o.Name))}");
        examined.Should().BeGreaterThan(schemas.Count, "every schema gave values to compare");
        disagreements.Should().BeEmpty();
    }

    /// <summary>One schema for the whole spec: its component schemas as $defs, and a root property for each.</summary>
    private static string Combined(List<KeyValuePair<string, JsonObject>> schemas)
    {
        var defs = new JsonObject();
        var properties = new JsonObject();
        foreach (var (name, schema) in schemas)
        {
            defs[name] = Rebase(schema.DeepClone());
            properties[name] = new JsonObject { ["$ref"] = $"#/$defs/{Pointer(name)}" };
        }

        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$defs"] = defs,
            ["properties"] = properties,
        }.ToJsonString();
    }

    /// <summary>
    /// OpenAPI's integer formats, which JSON Schema doesn't define, for the interpreting oracle: int32 and int64 are in
    /// OpenAPI's format registry, so like the discriminator they're written out here (Corvus knows them already).
    /// </summary>
    private static void OpenApiFormats()
    {
        static bool Within(JsonNode? node, decimal min, decimal max)
            => node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number || (decimal.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= min && d <= max);

        Json.Schema.Formats.Register(new RangeFormat("int32", node => Within(node, int.MinValue, int.MaxValue)));
        Json.Schema.Formats.Register(new RangeFormat("int64", node => Within(node, long.MinValue, long.MaxValue)));
    }

    private sealed class RangeFormat(string key, Func<JsonNode?, bool> valid) : Json.Schema.Format(key)
    {
        public override bool Validate(JsonNode? node, out string? errorMessage)
        {
            errorMessage = valid(node) ? null : $"The value is out of range for {Key}.";
            return errorMessage is null;
        }
    }

    private static string Short(JsonNode? value)
    {
        var text = value?.ToJsonString() ?? "null";
        return text.Length > 200 ? text[..200] + "…" : text;
    }

    /// <summary>
    /// The schema with its references to components moved to $defs, and OpenAPI's discriminator written as JSON Schema,
    /// which hasn't one: OpenAPI 3.1.1 says a value that matches no mapping can determine no schema and "validation SHOULD
    /// fail". So the property is required, is one of the mapped names, and each name selects its schema.
    /// </summary>
    private static JsonNode? Rebase(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                if (o["discriminator"]?["propertyName"]?.GetValue<string>() is { } property)
                {
                    var members = (o["oneOf"] ?? o["anyOf"]) as JsonArray ?? [];
                    var mapping = (o["discriminator"]?["mapping"] as JsonObject ?? []).Select(m => (Name: m.Key, Target: m.Value!.GetValue<string>())).ToList();
                    var implicitNames = members.Select(m => m?["$ref"]?.GetValue<string>()).OfType<string>()
                        .Where(target => mapping.All(m => m.Target != target)).Select(target => (Name: target[(target.LastIndexOf('/') + 1)..], Target: target));
                    var names = mapping.Concat(implicitNames).ToList();
                    o["allOf"] = new JsonArray(
                        new JsonObject
                        {
                            ["required"] = new JsonArray(property),
                            ["properties"] = new JsonObject { [property] = new JsonObject { ["enum"] = new JsonArray(names.Select(n => (JsonNode?)n.Name).ToArray()) } },
                        },
                        new JsonObject
                        {
                            ["allOf"] = new JsonArray(names.Select(n => (JsonNode?)new JsonObject
                            {
                                ["if"] = new JsonObject { ["properties"] = new JsonObject { [property] = new JsonObject { ["const"] = n.Name } } },
                                ["then"] = new JsonObject { ["$ref"] = n.Target },
                            }).ToArray()),
                        });
                }

                foreach (var (key, value) in o.ToList())
                {
                    if (key == "$ref" && value?.GetValue<string>() is { } reference && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                    {
                        o[key] = "#/$defs/" + reference["#/components/schemas/".Length..];
                    }
                    else
                    {
                        Rebase(value);
                    }
                }

                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    Rebase(item);
                }

                break;
        }

        return node;
    }

    private static string Pointer(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
