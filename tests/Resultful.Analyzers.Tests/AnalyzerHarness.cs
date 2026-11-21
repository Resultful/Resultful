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
    public static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers =
    [
        new MustObserveAnalyzer(), new DefaultResultAnalyzer(), new UncheckedValueAnalyzer(), new BareStringAnalyzer(), new UnassignedFieldAnalyzer(),
        new UntypedHandlerAnalyzer(), new MapToBindAnalyzer(),
    ];

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
    public static Compilation Compile(IEnumerable<string> sources, bool withCore = true, bool generate = false, bool executable = false)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"Test{i}.cs", encoding: Encoding.UTF8));
        var refs = withCore ? FrameworkRefs.Value.Add(Core) : FrameworkRefs.Value;
        Compilation compilation = CSharpCompilation.Create("Consumer", trees, refs,
            new CSharpCompilationOptions(executable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
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
    public static void Verify(string source, string? mustObserveTypes = null, bool withCore = true, bool generate = false, bool executable = false) =>
        Verify([source], mustObserveTypes, withCore, generate, executable);

    public static void Verify(IReadOnlyList<string> sources, string? mustObserveTypes = null, bool withCore = true, bool generate = false,
        bool executable = false)
    {
        var expected = Markers(sources);
        var ids = expected.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        // Generated shapes are analyzed too: the generator's own code must pass the rules.
        var diagnostics = Diagnose(Compile(sources, withCore, generate, executable), mustObserveTypes, analyzeGenerated: generate)
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

    /// <summary>
    /// Applies <paramref name="fix"/> (RF0001's by default) to the first diagnostic it fixes, or fix-all over the document, and
    /// returns the new text; with <paramref name="generate"/>, the generator's output is in the project too.
    /// </summary>
    public static string ApplyFix(string source, bool fixAll = false, string? mustObserveTypes = null, CodeFixProvider? fix = null,
        bool generate = false, int action = 0)
    {
        fix ??= Fix;
        using var workspace = new AdhocWorkspace();
        var document = MakeDocument(workspace, source, generate);
        var diagnostics = Fixable(document.Project, fix, mustObserveTypes);
        Assert.NotEmpty(diagnostics);

        Document changed;
        if (fixAll)
        {
            var context = new FixAllContext(document, fix, FixAllScope.Document, Actions(fix, document, diagnostics[0])[0].EquivalenceKey, fix.FixableDiagnosticIds,
                new Provider(p => Fixable(p, fix, mustObserveTypes)), CancellationToken.None);
            var all = fix.GetFixAllProvider()!.GetFixAsync(context).GetAwaiter().GetResult();
            Assert.NotNull(all);
            changed = Apply(all, document);
        }
        else
        {
            changed = Apply(Actions(fix, document, diagnostics[0])[action], document);
        }

        var errors = changed.Project.GetCompilationAsync().GetAwaiter().GetResult()!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "the fix broke the build:\n" + string.Join("\n", errors));
        return changed.GetTextAsync().GetAwaiter().GetResult().ToString();
    }

    /// <summary>The code actions' titles for each diagnostic in the source that the fix handles.</summary>
    public static List<string> FixTitles(string source, CodeFixProvider? fix = null, string? mustObserveTypes = null, bool generate = false)
    {
        fix ??= Fix;
        using var workspace = new AdhocWorkspace();
        var document = MakeDocument(workspace, source, generate);
        return [.. Fixable(document.Project, fix, mustObserveTypes).SelectMany(d => Actions(fix, document, d)).Select(a => a.Title)];
    }

    /// <summary>The code actions RF0001's fix offers for each diagnostic in the source.</summary>
    public static int CountFixes(string source, string? mustObserveTypes = null) => FixTitles(source, Fix, mustObserveTypes).Count;

    static Document MakeDocument(AdhocWorkspace workspace, string source, bool generate)
    {
        var compilation = Compile([source], generate: generate);
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Fix", "Fix", LanguageNames.CSharp,
            compilationOptions: compilation.Options, parseOptions: ParseOptions, metadataReferences: compilation.References));
        var document = project.AddDocument("Test0.cs", source, filePath: "Test0.cs");
        // The generator's output, as plain documents: each starts with `// <auto-generated/>`, so the rules skip it.
        foreach (var (tree, i) in compilation.SyntaxTrees.Skip(1).Select((t, i) => (t, i)))
            document = document.Project.AddDocument($"Generated{i}.cs", tree.GetText(), filePath: $"Generated{i}.cs").Project.GetDocument(document.Id)!;
        return document;
    }

    static List<CodeAction> Actions(CodeFixProvider fix, Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        fix.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None)).GetAwaiter().GetResult();
        return actions;
    }

    public static List<Diagnostic> Fixable(Project project, CodeFixProvider fix, string? mustObserveTypes) =>
        [.. project.GetCompilationAsync().GetAwaiter().GetResult()!.WithAnalyzers(Analyzers, Options(mustObserveTypes))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().Where(d => fix.FixableDiagnosticIds.Contains(d.Id))
            .OrderBy(d => d.Location.SourceSpan.Start)];

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
