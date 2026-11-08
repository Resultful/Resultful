using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Matchmark.Testing;
using Matchmark.Cli;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The sample corpus, measured: for each spec, what the generator has to skip and what the regression engine builds.
/// The ledger below is the current state. Supporting more of a sample changes it (update it, and say what got better);
/// so does breaking something that worked.
/// </summary>
[TestFixture]
public sealed class CorpusTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(ThisDirectory(), ".."));

    private static readonly (string Name, string Path, string[] Arazzo, string[] Overlays)[] Samples =
    [
        ("petstore", "Matchmark.Sample.Contract/petstore.yaml", ["Matchmark.Sample.Contract/petstore.arazzo.yaml"], []),
        ("museum", "samples/museum/openapi.yaml", ["samples/museum/arazzo/museum-api.arazzo.yaml", "samples/museum/arazzo/museum-tickets.arazzo.yaml"], []),
        ("orders", "samples/orders/openapi.yaml", ["samples/orders/orders.arazzo.yaml"], ["samples/orders/orders.overlay.yaml"]),
        ("bookings", "samples/bookings/openapi.yaml", [], ["samples/bookings/bookings.overlay.yaml"]),
        ("catalogue", "samples/catalogue/openapi.yaml", [], ["samples/catalogue/catalogue.overlay.yaml"]),
        ("payments", "samples/payments/openapi.yaml", [], []),
    ];

    [Test]
    public void The_ledger_of_what_each_sample_gets()
    {
        var ledger = Samples.Select(sample =>
        {
            var path = Path.Combine(Root, sample.Path);
            var text = File.ReadAllText(path);
            Scaffolder.Scaffold(text, Path.GetFileName(path), "Corpus.Api", "Corpus", "Corpus", controllers: true, out var warnings);
            var arazzo = sample.Arazzo.Select(a => SpecJson.Arazzo(File.ReadAllText(Path.Combine(Root, a)), a)).ToArray();
            var overlays = sample.Overlays.Select(o => (File.ReadAllText(Path.Combine(Root, o)), o)).ToList();
            var json = SpecJson.Normalize(text, path, out _, overlays);
            var suite = RegressionSuite.FromJson(json, arazzo);
            var runnable = suite.Arazzo.Count(w => suite.ArazzoGaps(w).Count == 0);
            var audit = AuditCommand.Measure(json, arazzo);
            return $"{sample.Name}: generator skips {warnings.Distinct().Count()}; suite {suite.Cases.Count} cases, {suite.Workflows.Count} workflows from links, "
                   + $"{runnable} of {suite.Arazzo.Count} Arazzo workflows runnable, {suite.Gaps.Count} gaps"
                   + (RegressionSuite.ForWebhooks(json) is { } webhooks ? $", {webhooks.Cases.Count} webhook cases; " : "; ")
                   + $"audit {audit.Findings.Count(f => f.Finding.Rule.Level == "warning")} warnings, {audit.Findings.Count} findings, {audit.Score * 100:0}%";
        }).ToList();

        File.WriteAllLines(Path.Combine(TestContext.CurrentContext.WorkDirectory, "corpus-ledger.txt"), ledger);
        ledger.Should().Equal(
            "petstore: generator skips 0; suite 90 cases, 1 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps; audit 6 warnings, 14 findings, 79%",
            "museum: generator skips 3; suite 100 cases, 0 workflows from links, 3 of 3 Arazzo workflows runnable, 0 gaps, 28 webhook cases; audit 12 warnings, 20 findings, 82%",
            "orders: generator skips 0; suite 90 cases, 2 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps, 19 webhook cases; audit 3 warnings, 8 findings, 91%",
            "bookings: generator skips 0; suite 81 cases, 1 workflows from links, 0 of 0 Arazzo workflows runnable, 0 gaps; audit 0 warnings, 1 findings, 100%",
            "catalogue: generator skips 0; suite 73 cases, 1 workflows from links, 0 of 0 Arazzo workflows runnable, 0 gaps; audit 1 warnings, 2 findings, 97%",
            "payments: generator skips 0; suite 93 cases, 1 workflows from links, 0 of 0 Arazzo workflows runnable, 0 gaps; audit 0 warnings, 1 findings, 100%");
    }

    /// <summary>What the generator writes for each sample compiles: the Contract and Server parts, against Resultful and ASP.NET Core.</summary>
    [TestCase("petstore")]
    [TestCase("museum")]
    [TestCase("orders")]
    [TestCase("bookings")]
    [TestCase("catalogue")]
    [TestCase("payments")]
    public void The_code_generated_for_each_sample_compiles(string name)
    {
        var sample = Samples.Single(s => s.Name == name);
        var spec = File.ReadAllText(Path.Combine(Root, sample.Path));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Resultful.Unit).Assembly.Location)
            .Append(typeof(OneOf.OneOf<>).Assembly.Location)
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(name, [], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], [new Text(sample.Path, spec)], optionsProvider: new Options(name));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        generated.SyntaxTrees.Should().NotBeEmpty();
        generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
    }

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options(string name) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _spec = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = Naming(name),
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = $"Corpus.{Naming(name)}",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract,Server",
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _spec;

        private static string Naming(string name) => char.ToUpperInvariant(name[0]) + name[1..];

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
