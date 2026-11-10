using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Cli;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// matchmark bundle (question 2, option b): a spec split across files becomes one the generator reads. References are
/// resolved relative to the file that makes them, targets hoisted by where they're used, and a cycle (an owner's pets
/// are pets) ends at the hoisted name.
/// </summary>
[TestFixture]
public sealed class BundleTests
{
    private static readonly string Split = Path.GetFullPath(Path.Combine(Directory(), "..", "samples", "split", "openapi.yaml"));

    [Test]
    public void A_split_spec_becomes_one_with_each_part_a_component()
    {
        var bundled = BundleCommand.Bundle(Split);
        var components = bundled["components"]!;
        components["schemas"]!.AsObject().Select(s => s.Key).Should().BeEquivalentTo("pet", "Owner", "Problem");
        components["parameters"]!["limit"]!["in"]!.GetValue<string>().Should().Be("query");
        components["responses"]!["Problem"]!["content"]!["application/problem+json"]!["schema"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/Problem");
        components["schemas"]!["Owner"]!["properties"]!["pets"]!["items"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/pet", "the cycle ends at the hoisted name");
        bundled.ToJsonString().Should().NotContain(".yaml", "every reference is local now");
    }

    [Test]
    public void The_bundle_generates_code_that_compiles()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "split-bundled.json");
        File.WriteAllText(path, BundleCommand.Bundle(Split).ToJsonString());
        var (_, diagnostics) = GeneratedServer.Build("Split", path, [], string.Empty, withRules: false);
        diagnostics.Should().NotContain(d => d.Id == "MM001" || d.Id == "MM002" || d.Id == "MM003");
    }

    /// <summary>Question 2, option (a): the generator reads the split spec itself, given its other files as MatchmarkSpecPart.</summary>
    [Test]
    public void The_generator_reads_a_split_spec_from_its_parts()
    {
        var folder = Path.GetDirectoryName(Split)!;
        var parts = new[] { "pet.yaml", "common/schemas.yaml", "common/parameters.yaml", "common/responses.yaml" }.Select(p => Path.Combine(folder, p)).ToList();
        var texts = new[] { Split }.Concat(parts).Select(p => (Microsoft.CodeAnalysis.AdditionalText)new Text(p, File.ReadAllText(p))).ToArray();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => (Microsoft.CodeAnalysis.MetadataReference)Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p));
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Split", [], references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: Microsoft.CodeAnalysis.NullableContextOptions.Enable));
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], texts, optionsProvider: new PartOptions(Split));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);

        diagnostics.Should().NotContain(d => d.Id == "MM000" || d.Id == "MM001" || d.Id == "MM003", string.Join("\n", diagnostics));
        generated.SyntaxTrees.Select(t => t.ToString()).Should().Contain(t => t.Contains("public sealed record Owner", StringComparison.Ordinal), "the schema from common/schemas.yaml is generated");
        generated.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()).Take(10).Should().BeEmpty();
    }

    private sealed class Text(string path, string text) : Microsoft.CodeAnalysis.AdditionalText
    {
        public override string Path => path;

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken cancellationToken = default) => Microsoft.CodeAnalysis.Text.SourceText.From(text);
    }

    private sealed class PartOptions(string root) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        private static readonly Values Spec = new(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "Split",
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = "Split.Api",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract,Server",
        });

        private static readonly Values Part = new(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.MatchmarkSpecPart"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "Split",
        });

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.SyntaxTree tree) => GlobalOptions;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.AdditionalText textFile) => textFile.Path == root ? Spec : Part;

        private sealed class Values(Dictionary<string, string> values) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
