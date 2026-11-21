using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Resultful.Generators;
using Xunit;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// Consumer code compiled in process against the real core (the Resultful.dll this project references), optionally
/// through the real generator, with the analyzers and an optional .editorconfig value. `// expect: RF0001` markers are
/// compared with the diagnostics both ways.
/// </summary>
public static partial class AnalyzerHarness
{
    public static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = [new MustObserveAnalyzer(), new DefaultResultAnalyzer()];

#if NET11_0_OR_GREATER
    public const bool IsNet11 = true;
#else
    public const bool IsNet11 = false;
#endif

    public static string Metadata(string key) =>
        typeof(AnalyzerHarness).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;

    static readonly MetadataReference Core = MetadataReference.CreateFromFile(typeof(Unit).Assembly.Location);

    static readonly Lazy<ImmutableArray<MetadataReference>> FrameworkRefs = new(() =>
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return [.. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => string.Equals(Path.GetDirectoryName(p), runtimeDir, StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    });

    // C# 15 unions need net11.0's runtime types; on net10.0 the consumer is C# 14, as the SDK gives it.
    public static CSharpParseOptions ParseOptions => CSharpParseOptions.Default
        .WithLanguageVersion(IsNet11 ? LanguageVersion.Preview : LanguageVersion.CSharp14);

    /// <summary>
    /// Compiles consumer sources (Test0.cs, Test1.cs, …) against the core, or alone with <paramref name="withCore"/>
    /// false; with <paramref name="generate"/>, through Resultful.Generators first.
    /// </summary>
    public static Compilation Compile(IEnumerable<string> sources, bool withCore = true, bool generate = false)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"Test{i}.cs", encoding: Encoding.UTF8));
        var refs = withCore ? FrameworkRefs.Value.Add(Core) : FrameworkRefs.Value;
        Compilation compilation = CSharpCompilation.Create("Consumer", trees, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        if (generate)
        {
            CSharpGeneratorDriver.Create([new ResultShapeGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
                .RunGeneratorsAndUpdateCompilation(compilation, out compilation, out var generatorDiagnostics);
            Assert.Empty(generatorDiagnostics);
        }

        return compilation;
    }

    /// <summary>The .editorconfig values; <paramref name="analyzeGenerated"/> sets `generated_code = false`, as the compatibility legs do.</summary>
    public static AnalyzerOptions Options(string? mustObserveTypes, bool analyzeGenerated = false)
    {
        var values = new Dictionary<string, string>();
        if (mustObserveTypes is not null) values[Descriptors.MustObserveTypesKey] = mustObserveTypes;
        if (analyzeGenerated) values["generated_code"] = "false";
        return new([], new ConfigProvider(values));
    }

    /// <summary>Compiler and analyzer diagnostics together.</summary>
    public static ImmutableArray<Diagnostic> Diagnose(Compilation compilation, string? mustObserveTypes = null, bool analyzeGenerated = false) =>
        compilation.WithAnalyzers(Analyzers, Options(mustObserveTypes, analyzeGenerated)).GetAllDiagnosticsAsync().GetAwaiter().GetResult();

    [GeneratedRegex(@"//\s*expect:\s*(?<ids>[A-Z]+[0-9]+(\s*,\s*[A-Z]+[0-9]+)*)")]
    private static partial Regex ExpectMarker();

    public static IReadOnlySet<(string File, int Line, string Id)> Markers(IReadOnlyList<string> sources)
    {
        var markers = new HashSet<(string, int, string)>();
        for (var f = 0; f < sources.Count; f++)
        {
            var lines = sources[f].Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (ExpectMarker().Match(lines[i]) is { Success: true } m)
                    foreach (var id in m.Groups["ids"].Value.Split(',', StringSplitOptions.TrimEntries))
                        markers.Add(($"Test{f}.cs", i + 1, id));
        }

        return markers;
    }

    static (string File, int Line, string Id) Key(Diagnostic d)
    {
        var span = d.Location.GetMappedLineSpan();
        return (span.Path, span.StartLinePosition.Line + 1, d.Id);
    }

    /// <summary>
    /// Every marker is reported on its line, and nothing else is: no other RF diagnostic, no error, no warning a
    /// marker names elsewhere.
    /// </summary>
    public static void Verify(string source, string? mustObserveTypes = null, bool withCore = true, bool generate = false) =>
        Verify([source], mustObserveTypes, withCore, generate);

    public static void Verify(IReadOnlyList<string> sources, string? mustObserveTypes = null, bool withCore = true, bool generate = false)
    {
        var expected = Markers(sources);
        var ids = expected.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        // Generated shapes are analyzed too: the generator's own code must pass the rules.
        var diagnostics = Diagnose(Compile(sources, withCore, generate), mustObserveTypes, analyzeGenerated: generate)
            .Where(d => d.Location.IsInSource)
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id.StartsWith("RF", StringComparison.Ordinal) || ids.Contains(d.Id))
            .ToList();
        var actual = diagnostics.Select(Key).ToHashSet();
        var problems = expected.Where(m => !actual.Contains(m)).Select(m => $"expected {m.Id} at {m.File}:{m.Line}, not reported")
            .Concat(diagnostics.Where(d => !expected.Contains(Key(d))).Select(d => $"unexpected {d}"))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---- code fixes ----

    public static readonly MustObserveCodeFixProvider Fix = new();

    /// <summary>Applies RF0001's fix to the first diagnostic, or fix-all over the document, and returns the new text.</summary>
    public static string ApplyFix(string source, bool fixAll = false, string? mustObserveTypes = null)
    {
        using var workspace = new AdhocWorkspace();
        var compilation = Compile([source]);
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Fix", "Fix", LanguageNames.CSharp,
            compilationOptions: compilation.Options, parseOptions: ParseOptions, metadataReferences: compilation.References));
        var document = project.AddDocument("Test0.cs", source, filePath: "Test0.cs");
        var diagnostics = Rf0001(document.Project, mustObserveTypes);
        Assert.NotEmpty(diagnostics);

        Document changed;
        if (fixAll)
        {
            var context = new FixAllContext(document, Fix, FixAllScope.Document, nameof(MustObserveCodeFixProvider), [Descriptors.MustObserve.Id],
                new Provider(p => Rf0001(p, mustObserveTypes)), CancellationToken.None);
            var action = Fix.GetFixAllProvider().GetFixAsync(context).GetAwaiter().GetResult();
            Assert.NotNull(action);
            changed = Apply(action, document);
        }
        else
        {
            var actions = new List<CodeAction>();
            Fix.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[0], (a, _) => actions.Add(a), CancellationToken.None)).GetAwaiter().GetResult();
            Assert.Single(actions);
            Assert.Equal(MustObserveCodeFixProvider.Title, actions[0].Title);
            changed = Apply(actions[0], document);
        }

        var errors = changed.Project.GetCompilationAsync().GetAwaiter().GetResult()!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "the fix broke the build:\n" + string.Join("\n", errors));
        return changed.GetTextAsync().GetAwaiter().GetResult().ToString();
    }

    /// <summary>The code actions RF0001's fix offers for each diagnostic in the source.</summary>
    public static int CountFixes(string source, string? mustObserveTypes = null)
    {
        using var workspace = new AdhocWorkspace();
        var compilation = Compile([source]);
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Fix", "Fix", LanguageNames.CSharp,
            compilationOptions: compilation.Options, parseOptions: ParseOptions, metadataReferences: compilation.References));
        var document = project.AddDocument("Test0.cs", source, filePath: "Test0.cs");
        var count = 0;
        foreach (var diagnostic in Rf0001(document.Project, mustObserveTypes))
            Fix.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (_, _) => count++, CancellationToken.None)).GetAwaiter().GetResult();
        return count;
    }

    public static List<Diagnostic> Rf0001(Project project, string? mustObserveTypes) =>
        [.. project.GetCompilationAsync().GetAwaiter().GetResult()!.WithAnalyzers(Analyzers, Options(mustObserveTypes))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().Where(d => d.Id == Descriptors.MustObserve.Id)];

    static Document Apply(CodeAction action, Document document)
    {
        var operations = action.GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult();
        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
    }

    sealed class Provider(Func<Project, List<Diagnostic>> diagnose) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnose(document.Project).Where(d => d.Location.SourceTree?.FilePath == document.FilePath));

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnose(project));
    }

    sealed class ConfigProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        readonly Options _options = new(values);
        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;

        sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
