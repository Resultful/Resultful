using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>OpenAPI Overlays applied before anything reads the spec: the generator, the verifier, the scaffolder and the engine.</summary>
[TestFixture]
public sealed class OverlayTests
{
    [Test]
    public void The_traits_example_from_Overlay_1_2_applies_by_trait()
    {
        const string spec = """
            openapi: 3.2.0
            info: { title: API with a paged collection, version: 1.0.0 }
            paths:
              /items:
                get:
                  x-oai-traits: ['paged']
                  responses: { '200': { description: OK } }
              /items/{id}/subitems:
                get:
                  x-oai-traits: ['paged']
                  parameters: [{ name: id, in: path, required: true }]
                  responses: { '200': { description: OK } }
              /other:
                get:
                  responses: { '200': { description: OK } }
            """;
        const string overlay = """
            overlay: 1.2.0
            info: { title: Apply Traits, version: 1.0.0 }
            actions:
              - target: $.paths[?(@.get['x-oai-traits'][?(@ == 'paged')])].get
                update:
                  parameters:
                    - { name: top, in: query }
                    - { name: skip, in: query }
            """;

        var result = JsonNode.Parse(Overlay.Apply(spec, "spec.yaml", [(overlay, "traits.yaml")]))!;

        Names(result["paths"]!["/items"]!["get"]!).Should().Equal("top", "skip");
        Names(result["paths"]!["/items/{id}/subitems"]!["get"]!).Should().Equal("id", "top", "skip");
        result["paths"]!["/other"]!["get"]!["parameters"].Should().BeNull();
    }

    [Test]
    public void Remove_and_copy_and_merges_into_objects()
    {
        const string spec = """
            { "openapi": "3.1.0", "info": { "title": "T", "version": "1" }, "paths": {}, "components": { "schemas": {
              "A": { "type": "object", "properties": { "a": { "type": "string" }, "secret": { "type": "string" } } },
              "B": { "type": "object", "properties": { "b": { "type": "integer" } } } } } }
            """;
        const string overlay = """
            { "overlay": "1.2.0", "info": { "title": "o", "version": "1" }, "actions": [
              { "target": "$.components.schemas.A.properties.secret", "remove": true },
              { "target": "$.components.schemas.A.properties", "copy": "$.components.schemas.B.properties" },
              { "target": "$..properties.a", "update": { "maxLength": 10 } } ] }
            """;

        var schemas = JsonNode.Parse(Overlay.Apply(spec, "spec.json", [(overlay, "o.json")]))!["components"]!["schemas"]!;

        schemas["A"]!["properties"]!.AsObject().Select(p => p.Key).Should().Equal("a", "b");
        schemas["A"]!["properties"]!["a"]!["maxLength"]!.GetValue<int>().Should().Be(10);
    }

    [Test]
    public void The_generator_reads_the_spec_with_its_overlays_and_extensions_survive()
    {
        const string spec = """
            openapi: 3.1.0
            info: { title: T, version: '1' }
            paths:
              /things:
                get:
                  operationId: listThings
                  responses: { '200': { description: ok, content: { application/json: { schema: { $ref: '#/components/schemas/Thing' } } } } }
            components:
              schemas:
                Thing: { type: object, properties: { name: { type: string } } }
            """;
        const string overlay = """
            overlay: 1.0.0
            info: { title: More, version: '1' }
            actions:
              - target: $.components.schemas.Thing.properties
                update: { colour: { type: string, maxLength: 20 } }
              - target: $.paths['/things'].get
                update: { x-matchmark-ensures: [{ condition: $statusCode == 200 }] }
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var driver = CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()],
            [new Text("/specs/t.yaml", spec), new Text("/specs/t.overlay.yaml", overlay)], optionsProvider: new Options());
        var sources = driver.RunGenerators(CSharpCompilation.Create("T", [], references)).GetRunResult().Results
            .SelectMany(r => r.GeneratedSources).ToDictionary(s => s.HintName, s => s.SourceText.ToString());

        sources["T.Models.g.cs"].Should().Contain("Colour").And.Contain("MaxLength(20)");
        JsonNode.Parse(SpecJson.Normalize(spec, "t.yaml", out _, [(overlay, "t.overlay.yaml")]))!["paths"]!["/things"]!["get"]!["x-matchmark-ensures"]![0]!["condition"]!
            .GetValue<string>().Should().Be("$statusCode == 200");
    }

    private static IEnumerable<string> Names(JsonNode operation) => operation["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>());

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    /// <summary>The spec marked Matchmark, the overlay marked MatchmarkOverlay, both for the API "T".</summary>
    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values(new Dictionary<string, string>
        {
            [textFile.Path.Contains("overlay", StringComparison.Ordinal) ? "build_metadata.AdditionalFiles.MatchmarkOverlay" : "build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "T",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract",
        });

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
