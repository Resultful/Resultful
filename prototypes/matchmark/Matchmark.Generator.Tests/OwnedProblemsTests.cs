using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// MatchmarkProblems="Owned": the contract generates the spec's own problem types instead of using ASP.NET Core's, so a
/// client-only package needs no ASP.NET Core. The Pet Store contract, compiled with no ASP.NET Core assembly at all.
/// </summary>
[TestFixture]
public sealed class OwnedProblemsTests
{
    private static readonly string Spec = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "Matchmark.Sample.Contract", "petstore.yaml"));

    [Test]
    public void A_contract_that_owns_its_problem_types_compiles_without_ASP_NET_Core()
    {
        var (generated, diagnostics) = Generate("Contract");

        diagnostics.Should().BeEmpty();
        generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
        var source = string.Join("\n", generated.SyntaxTrees.Select(t => t.ToString()));
        source.Should().NotContain("Microsoft.AspNetCore")
            .And.Contain("public sealed record ProblemDetails")
            .And.Contain("public sealed record HttpValidationProblemDetails");
    }

    [Test]
    public void The_server_part_needs_ASP_NET_Core_s_problem_types()
    {
        var (generated, diagnostics) = Generate("Contract,Server");

        diagnostics.Select(d => d.Id).Should().Equal("MM026");
        generated.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath)).Should().NotContain(f => f.Contains("Endpoints", StringComparison.Ordinal));
    }

    private static (Compilation Generated, IReadOnlyList<Diagnostic> Diagnostics) Generate(string parts)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !Path.GetFileName(path).StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Append(typeof(Resultful.Unit).Assembly.Location)
            .Append(typeof(OneOf.OneOf<>).Assembly.Location)
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Owned", [], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], [new Text(Spec, File.ReadAllText(Spec))],
            optionsProvider: new Options(parts));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        return (generated, diagnostics.ToList());
    }

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options(string parts) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _spec = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "PetStore",
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = "PetStore.Client",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = parts,
            ["build_metadata.AdditionalFiles.MatchmarkProblems"] = "Owned",
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _spec;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
