using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Specs.Overlay;
using NUnit.Framework;

namespace Matchmark.Specs.Tests;

/// <summary>
/// The Overlay reader and applier against the specification (specs/overlay): every pass fixture reads with no problem
/// and every fail fixture with one, as the official schema judges them too; every compliant set's overlay, applied to its
/// input, gives its output; and every example in the specification's text reads.
/// </summary>
[TestFixture]
public sealed class OverlayTests
{
    public static IEnumerable<TestCaseData> Fixtures()
        => Specs.Files("overlay/tests", ".yaml").Select(f => new TestCaseData(f, f.Contains("/pass/", StringComparison.Ordinal)).SetName($"Overlay fixture {f}"));

    [TestCaseSource(nameof(Fixtures))]
    public void Each_fixture_is_read_as_the_specification_says(string relative, bool valid)
    {
        var result = OverlayReader.Read(Specs.Load(relative));

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
        // The oracle: where the specification publishes a schema (1.0 and 1.1), it judges the fixture as its folder says.
        var version = relative.Split('/')[2];
        if (version is not ("v1.0" or "v1.1"))
        {
            Assert.Ignore($"No published schema for {version}.");
        }

        var schema = Json.Schema.JsonSchema.FromText(Specs.Load($"overlay/schemas/{version}/schema.yaml").ToJsonString());
        schema.Evaluate(Specs.Load(relative), new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Flag }).IsValid.Should().Be(valid);
    }

    public static IEnumerable<string> CompliantSets() => Directory.GetDirectories(Specs.PathOf("overlay", "compliant-sets")).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal);

    [TestCaseSource(nameof(CompliantSets))]
    public void Each_compliant_set_s_overlay_gives_its_output(string set)
    {
        var folder = $"overlay/compliant-sets/{set}";
        var document = Specs.Load($"{folder}/openapi.yaml");
        var overlay = OverlayReader.Read(Specs.Load($"{folder}/overlay.yaml"));
        overlay.Problems.Should().BeEmpty();

        OverlayApplier.Apply(document, overlay.Document!);

        JsonNode.DeepEquals(Reparsed(document), Reparsed(Specs.Load($"{folder}/output.yaml"))).Should().BeTrue($"{set} gives\n{document.ToJsonString()}");
    }

    public static IEnumerable<TestCaseData> TextExamples()
        => Specs.Files("overlay/text", ".md").Where(f => !f.Contains("-editors", StringComparison.Ordinal)).SelectMany(f =>
            // Whole documents: the text also shows excerpts (an 'extends' alone) that illustrate one field and have no actions.
            Specs.Examples(File.ReadAllText(Specs.PathOf(f))).Where(e => e.TrimStart().StartsWith("overlay:", StringComparison.Ordinal) && e.Contains("\nactions:", StringComparison.Ordinal))
                .Select((e, i) => new TestCaseData(f, i, e).SetName($"Overlay example {i} in {f}")));

    [TestCaseSource(nameof(TextExamples))]
    public void Each_example_in_the_specification_s_text_reads(string text, int index, string example)
    {
        var result = OverlayReader.Read(Documents.Load(example, $"{text}#{index}.yaml"));

        result.Problems.Select(p => p.ToString()).Should().BeEmpty();
    }

    [Test]
    public void Every_kind_of_document_is_examined()
    {
        Fixtures().Should().HaveCountGreaterThanOrEqualTo(132);
        CompliantSets().Should().HaveCountGreaterThan(5);
        TextExamples().Should().HaveCountGreaterThan(10);
    }

    [Test]
    public void A_reusable_action_reference_reads_as_the_action_it_refers_to()
    {
        var result = OverlayReader.Read(Specs.Load("overlay/tests/v1.2-dev/pass/reusable-action-reference-escaped-component-key.yaml"));

        result.Document!.Actions.Should().ContainSingle().Which.Should().Match<OverlayAction>(a =>
            a.Target == "$.paths.*.get.responses" && a.Reference == "#/components/actions/error-response~1v1~0beta" && a.Update!["404"] != null);
    }

    private static JsonNode? Reparsed(JsonNode? node) => node is null ? null : JsonNode.Parse(node.ToJsonString());
}
