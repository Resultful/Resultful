using System.Reflection;
using System.Runtime.Loader;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Matchmark.Generator.Tests;

/// <summary>A sample's generated Contract and Server parts, with its overlays, compiled with the given source and loaded.</summary>
internal static class GeneratedServer
{
    public static (Assembly Assembly, IReadOnlyList<Diagnostic> Diagnostics) Build(string api, string spec, IEnumerable<string> overlays, string source, bool withRules, bool resultful = false, bool clientOnNet = false)
    {
        var (assembly, diagnostics, _) = BuildWithSources(api, spec, overlays, source, withRules, resultful, clientOnNet);
        return (assembly, diagnostics);
    }

    /// <summary>As <see cref="Build"/>, with the generated sources too (hint name and text, by hint name), for derived evidence.</summary>
    public static (Assembly Assembly, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<(string Hint, string Text)> Sources) BuildWithSources(
        string api, string spec, IEnumerable<string> overlays, string source, bool withRules, bool resultful = false, bool clientOnNet = false)
    {
        var (assembly, diagnostics, sources, _) = BuildWithWarnings(api, spec, overlays, source, withRules, resultful, clientOnNet);
        return (assembly, diagnostics, sources);
    }

    /// <summary>
    /// As <see cref="BuildWithSources"/>, with the compiler's warnings in the generated sources too, at every warning level
    /// (nullable included): the emitted-code gate's input.
    /// </summary>
    public static (Assembly Assembly, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<(string Hint, string Text)> Sources, IReadOnlyList<Diagnostic> Warnings) BuildWithWarnings(
        string api, string spec, IEnumerable<string> overlays, string source, bool withRules, bool resultful = false, bool clientOnNet = false)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Resultful.Unit).Assembly.Location)
            .Append(typeof(OneOf.OneOf<>).Assembly.Location)
            .Concat(withRules ? [typeof(Matchmark.Rules.Cel).Assembly.Location] : [])
            .Where(path => withRules || !path.EndsWith("Matchmark.Rules.dll", StringComparison.Ordinal))
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create($"{api}{(withRules ? "WithRules" : "WithoutRules")}{Guid.NewGuid():N}", [CSharpSyntaxTree.ParseText(source, parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, warningLevel: 9999));
        var texts = new[] { spec }.Concat(overlays).Select(path => (AdditionalText)new Text(path, File.ReadAllText(path))).ToArray();
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], texts, optionsProvider: new Options(api, resultful, clientOnNet), parseOptions: parse);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        var sources = driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).OrderBy(s => s.HintName, StringComparer.Ordinal)
            .Select(s => (s.HintName, s.SourceText.ToString())).ToList();

        using var stream = new MemoryStream();
        var emitted = generated.Emit(stream);
        emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
        stream.Position = 0;
        var warnings = emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning && d.Location.SourceTree is { } tree && !compilation.SyntaxTrees.Contains(tree)).ToList();
        return (AssemblyLoadContext.Default.LoadFromStream(stream), diagnostics.ToList(), sources, warnings);
    }

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    /// <summary>The spec's metadata: plain outcomes by default (Q12), or on Resultful and OneOf for a source written against them.</summary>
    private sealed class Options(string api, bool resultful, bool clientOnNet) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _spec = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.MatchmarkClientValidation"] = clientOnNet ? "Net" : "Matchmark",
            ["build_metadata.AdditionalFiles.MatchmarkOutcomes"] = resultful ? "Resultful" : "Plain",
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = api,
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = $"{api}.Api",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract,Server",
        });

        private readonly AnalyzerConfigOptions _overlay = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.MatchmarkOverlay"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = api,
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            => System.IO.Path.GetFileName(textFile.Path).Contains("overlay", StringComparison.Ordinal) ? _overlay : _spec;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
