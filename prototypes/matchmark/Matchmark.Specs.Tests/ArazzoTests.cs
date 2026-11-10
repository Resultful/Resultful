using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Specs.Arazzo;
using NUnit.Framework;

namespace Matchmark.Specs.Tests;

/// <summary>
/// The Arazzo reader against the specification (specs/arazzo): every 1.1 pass fixture reads with no problem and every
/// fail fixture with one, as the official schema judges them; every example document and every example in the texts
/// reads; and on every mutation of every valid document, the reader and the official schema agree.
/// </summary>
[TestFixture]
public sealed class ArazzoTests
{
    public static IEnumerable<TestCaseData> Fixtures()
        => Specs.Files("arazzo/tests", ".yaml").Select(f => new TestCaseData(f, f.Contains("/pass/", StringComparison.Ordinal)).SetName($"Arazzo fixture {f}"));

    /// <summary>Every Arazzo document among the examples (they sit beside the OpenAPI and AsyncAPI documents they use).</summary>
    public static IEnumerable<string> Examples() => Specs.Files("arazzo/examples", ".yaml", ".yml", ".json").Where(f => Specs.Load(f)["arazzo"] is not null);

    public static IEnumerable<TestCaseData> TextExamples()
        => Specs.Files("arazzo/text", ".md").SelectMany(f => Specs.Examples(File.ReadAllText(Specs.PathOf(f)))
            .Where(e => e.TrimStart().StartsWith("arazzo:", StringComparison.Ordinal) && e.Contains("\nworkflows:", StringComparison.Ordinal))
            .Select((e, i) => new TestCaseData(f, i, e).SetName($"Arazzo example {i} in {f}")));

    /// <summary>The document's version's official schema.</summary>
    private static Json.Schema.JsonSchema Schema(JsonNode document)
        => Json.Schema.JsonSchema.FromText(Specs.Load(document["arazzo"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String
            && v.GetValue<string>().StartsWith("1.0", StringComparison.Ordinal) ? "arazzo/schemas/1.0.json" : "arazzo/schemas/1.1.yaml").ToJsonString());

    [TestCaseSource(nameof(Fixtures))]
    public void Each_fixture_is_read_as_the_specification_says(string relative, bool valid)
    {
        var result = ArazzoReader.Read(Specs.Load(relative));

        if (valid)
        {
            result.Problems.Select(p => p.ToString()).Should().BeEmpty();
            result.Document.Should().NotBeNull();
        }
        else
        {
            result.Problems.Should().NotBeEmpty("the fixture is one the specification says is invalid");
        }
    }

    [TestCaseSource(nameof(Fixtures))]
    public void The_official_schema_agrees_with_each_fixture(string relative, bool valid)
    {
        var document = Specs.Load(relative);
        if (document is not JsonObject { } obj || obj["arazzo"] is not JsonValue)
        {
            valid.Should().BeFalse();
            return;
        }

        Schema(document).Evaluate(document, new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Flag }).IsValid.Should().Be(valid);
    }

    [TestCaseSource(nameof(Examples))]
    public void Each_example_reads(string relative)
        => ArazzoReader.Read(Specs.Load(relative)).Problems.Select(p => p.ToString()).Should().BeEmpty();

    /// <summary>
    /// The texts' examples the official schema rejects, which the reader must reject too: 1.1.0's complete example gives two
    /// AsyncAPI steps an operationPath, which the 1.1 schema allows only as operationId or channelPath (an upstream finding).
    /// </summary>
    private static readonly (string Text, int Index)[] InvalidInTheirText = [("arazzo/text/1.1.0.md", 0)];

    [TestCaseSource(nameof(TextExamples))]
    public void Each_example_in_the_specification_s_text_reads_as_its_schema_judges_it(string text, int index, string example)
    {
        var document = Documents.Load(example, $"{text}#{index}.yaml");
        var schemaValid = Schema(document).Evaluate(document, new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Flag }).IsValid;
        var problems = ArazzoReader.Read(document).Problems;

        schemaValid.Should().Be(!InvalidInTheirText.Contains((text, index)), "the pinned list names the examples the schema rejects");
        (problems.Count == 0).Should().Be(schemaValid, string.Join("; ", problems));
    }

    /// <summary>The valid documents mutated: the pass fixtures, the examples, and the texts' examples.</summary>
    public static IEnumerable<TestCaseData> ValidDocuments()
        => Fixtures().Where(f => (bool)f.Arguments[1]!).Select(f => (string)f.Arguments[0]!).Concat(Examples())
            .Select(f => new TestCaseData(f).SetName($"Arazzo mutations of {f}"));

    [TestCaseSource(nameof(ValidDocuments))]
    public void On_every_mutation_the_reader_and_the_official_schema_agree(string relative)
    {
        var document = Specs.Load(relative);

        var disagreements = Mutations.Disagreements(document, d => ArazzoReader.Read(d).Problems, Schema(document));
        disagreements.Should().BeEmpty(string.Join("\n", disagreements));
    }

    [Test]
    public void Every_kind_of_document_is_examined()
    {
        Fixtures().Should().HaveCountGreaterThan(70);
        Examples().Should().HaveCountGreaterThanOrEqualTo(6);
        ValidDocuments().Sum(d => Mutations.Of(Specs.Load((string)d.Arguments[0]!)).Count()).Should().BeGreaterThan(5000);
    }
}
