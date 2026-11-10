using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Specs.AsyncApi;
using NUnit.Framework;

namespace Matchmark.Specs.Tests;

/// <summary>
/// The AsyncAPI reader against the specification (specs/asyncapi): every real-world example reads, as the official schema
/// judges it too; the 2.6 and 3.0 documents, forward-ported, read and pass the 3.1 schema; and on every mutation of every
/// example, the reader and the official schema agree, outside what the reader leaves to others (protocol bindings, x-
/// extensions, schemas in other formats).
/// </summary>
[TestFixture]
public sealed class AsyncApiTests
{
    public static IEnumerable<string> Examples() => Specs.Files("asyncapi/examples", ".yml", ".yaml").Where(f => Specs.Load(f)["asyncapi"] is not null);

    private static readonly Dictionary<string, Json.Schema.JsonSchema> Schemas = new();

    private static Json.Schema.JsonSchema Schema(string version)
    {
        lock (Schemas)
        {
            return Schemas.TryGetValue(version, out var schema)
                ? schema
                : Schemas[version] = Json.Schema.JsonSchema.FromText(Official(version).ToJsonString());
        }
    }

    /// <summary>
    /// The official schema, with the Avro schema format's definition left open (to anything but a Reference): it refers to
    /// itself in a way JsonSchema.Net refuses as circular, and schemas in other formats are among what the reader leaves to others.
    /// </summary>
    private static JsonNode Official(string version)
    {
        var schema = Specs.Load($"asyncapi/schemas/{version}.json");
        // Open, but not to a Reference Object: the schema offers a Reference beside it in a oneOf, which must still pick one.
        schema["definitions"]!["avroSchema_v1"] = new JsonObject { ["not"] = new JsonObject { ["required"] = new JsonArray("$ref") } };
        return schema;
    }

    private static bool SchemaValid(JsonNode document)
        => Schema(document["asyncapi"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String && v.GetValue<string>() == "3.0.0" ? "3.0.0" : "3.1.0")
            .Evaluate(document, new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Flag }).IsValid;

    [TestCaseSource(nameof(Examples))]
    public void Each_example_reads_as_the_official_schema_judges_it(string relative)
    {
        var document = Specs.Load(relative);
        var result = AsyncApiReader.Read(document);

        SchemaValid(document).Should().BeTrue();
        result.Problems.Select(p => p.ToString()).Should().BeEmpty();
        result.Document!.Operations.Should().NotBeEmpty();
    }

    [TestCase("samples/versions/asyncapi-2.6.yaml")]
    [TestCase("samples/versions/asyncapi-3.0.yaml")]
    [TestCase("samples/notifications/asyncapi.yaml")]
    public void Older_documents_forward_ported_read_and_pass_the_latest_schema(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Specs.Root, "..", relative));
        var upgraded = AsyncApiUpgrade.Upgrade((JsonObject)Documents.Load(File.ReadAllText(path), path)).Document;

        upgraded["asyncapi"]!.GetValue<string>().Should().Be(AsyncApiUpgrade.Latest);
        AsyncApiReader.Read(upgraded).Problems.Select(p => p.ToString()).Should().BeEmpty();
        SchemaValid(upgraded).Should().BeTrue();
    }

    // The specification's texts hold fragments of single objects, never a whole document, so they aren't read whole here.

    [TestCaseSource(nameof(Examples))]
    public void On_every_mutation_the_reader_and_the_official_schema_agree(string relative)
    {
        var document = Specs.Load(relative);
        var options = new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Flag };
        var schema = Schema(document["asyncapi"]!.GetValue<string>() == "3.0.0" ? "3.0.0" : "3.1.0");
        var disagreements = new List<string>();
        var compared = 0;
        foreach (var (label, mutated) in Mutations.Of(document))
        {
            // What the reader leaves to others: a binding's contents (each binding's own schema), extensions, other schema formats.
            if (label.Contains("/bindings/", StringComparison.Ordinal) || label.Contains("/x-", StringComparison.Ordinal) || label.Contains("/schemaFormat", StringComparison.Ordinal))
            {
                continue;
            }

            compared++;
            var problems = AsyncApiReader.Read(mutated).Problems;
            var valid = schema.Evaluate(mutated, options).IsValid;
            if ((problems.Count == 0) != valid)
            {
                disagreements.Add($"{label}: the schema says {(valid ? "valid" : "invalid")}, the reader {(problems.Count == 0 ? "valid" : string.Join("; ", problems))}");
            }
        }

        TestContext.Out.WriteLine($"{relative}: {compared} mutations compared");
        compared.Should().BeGreaterThan(20);
        disagreements.Should().BeEmpty(string.Join("\n", disagreements.Take(60)));
    }

    [Test]
    public void Every_kind_of_document_is_examined() => Examples().Should().HaveCountGreaterThanOrEqualTo(19);
}
