using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Large public specs (GitHub's, Stripe's), fetched rather than committed (samples/large/fetch.sh), for scale: the share
/// of operations generated, a number that should only rise, and that what is generated compiles. Runs when
/// MATCHMARK_LARGE_SPECS names the directory they were fetched to; otherwise it's skipped.
/// </summary>
[TestFixture]
public sealed partial class LargeSpecTests
{
    private static readonly string? Directory = Environment.GetEnvironmentVariable("MATCHMARK_LARGE_SPECS");

    private static IEnumerable<string> Specs()
        => Directory is null || !System.IO.Directory.Exists(Directory) ? ["(none fetched)"] : System.IO.Directory.GetFiles(Directory, "*.json").Order();

    [TestCaseSource(nameof(Specs))]
    public void Each_large_spec_generates_code_that_compiles(string path)
    {
        if (!File.Exists(path))
        {
            Assert.Ignore("Set MATCHMARK_LARGE_SPECS to a directory of specs fetched by samples/large/fetch.sh.");
        }

        var text = File.ReadAllText(path);
        var operations = (JsonNode.Parse(text)!["paths"] as JsonObject ?? []).Sum(p => (p.Value as JsonObject ?? []).Count(m => m.Key is "get" or "put" or "post" or "delete" or "patch"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Resultful.Unit).Assembly.Location).Append(typeof(OneOf.OneOf<>).Assembly.Location).Distinct()
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Large", [], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], [new Text(path, text)], optionsProvider: new Options());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);

        var skipped = diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Select(m => SkippedOperation().Match(m)).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().Count();
        var errors = generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var line = $"{Path.GetFileNameWithoutExtension(path)}: {operations} operations, {operations - skipped} generated ({100.0 * (operations - skipped) / operations:0.#}%), {errors.Count} compile errors";
        TestContext.Out.WriteLine(line);
        File.AppendAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "large-ledger.txt"), line + Environment.NewLine);

        diagnostics.Should().NotContain(d => d.Id == "CS8785", "the generator doesn't fail");
        errors.Select(e => e.ToString()).Take(20).Should().BeEmpty();
    }

    [GeneratedRegex(@"^Operation '([^']+)'.* was skipped")]
    private static partial Regex SkippedOperation();

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _spec = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "Large",
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = "Large.Api",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract,Server",
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _spec;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
