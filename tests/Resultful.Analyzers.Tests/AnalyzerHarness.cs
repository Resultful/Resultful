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
        new MustObserveAnalyzer(), new DefaultResultAnalyzer(), new UncheckedValueAnalyzer(), new BareStringAnalyzer(), new MatchToSwitchAnalyzer(),
        new UnassignedFieldAnalyzer(), new ClosedHierarchyAnalyzer(), new UntypedHandlerAnalyzer(), new MapToBindAnalyzer(), new ClosedHierarchySuppressor(),
        new FailedValueAnalyzer(),
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
    public static Compilation Compile(IEnumerable<string> sources, bool withCore = true, bool generate = false, bool executable = false,
        string? library = null, string? dependent = null, string? aliased = null)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"Test{i}.cs", encoding: Encoding.UTF8));
        var refs = withCore ? FrameworkRefs.Value.Add(Core) : FrameworkRefs.Value;
        if (aliased is not null)
        {
            // An unrelated assembly referenced under `extern alias O`, whose types may share the library's names.
            var other = CSharpCompilation.Create("Other", [CSharpSyntaxTree.ParseText(aliased, ParseOptions, path: "Other.cs", encoding: Encoding.UTF8)], refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            refs = refs.Add(Image(other).WithAliases(["O"]));
        }

        if (library is not null)
        {
            // A second assembly the consumer references, built through the generator (RF0010's foreign derived types).
            var lib = CSharpCompilation.Create("Library", [CSharpSyntaxTree.ParseText(library, ParseOptions, path: "Library.cs", encoding: Encoding.UTF8)], refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            CSharpGeneratorDriver.Create([new ResultShapeGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
                .RunGeneratorsAndUpdateCompilation(lib, out var built, out _);
            Assert.Empty(built.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            refs = refs.Add(Image(built));
            if (dependent is not null)
            {
                // A third assembly that references the library (RFSP0001 looks for derived types there too).
                var dep = CSharpCompilation.Create("Dependent", [CSharpSyntaxTree.ParseText(dependent, ParseOptions, path: "Dependent.cs", encoding: Encoding.UTF8)], refs,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
                Assert.Empty(dep.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
                refs = refs.Add(Image(dep));
            }
        }

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

    /// <summary>The assembly as a compiler reads a referenced DLL: from metadata, which shows only its public types by default.</summary>
    static MetadataReference Image(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    /// <summary>The .editorconfig values; <paramref name="analyzeGenerated"/> sets `generated_code = false`, as the compatibility legs do.</summary>
    public static AnalyzerOptions Options(string? mustObserveTypes, bool analyzeGenerated = false, string? closedHierarchyTypes = null)
    {
        var values = new Dictionary<string, string>();
        if (mustObserveTypes is not null) values[Descriptors.MustObserveTypesKey] = mustObserveTypes;
        if (closedHierarchyTypes is not null) values[Descriptors.ClosedHierarchyTypesKey] = closedHierarchyTypes;
        if (analyzeGenerated) values["generated_code"] = "false";
        return new([], new ConfigProvider(values));
    }

    /// <summary>
    /// Compiler and analyzer diagnostics together; with <paramref name="reportSuppressed"/>, also those a suppressor
    /// suppressed (<see cref="Diagnostic.IsSuppressed"/>), as Roslyn's own suppressor tests read them.
    /// </summary>
    public static ImmutableArray<Diagnostic> Diagnose(Compilation compilation, string? mustObserveTypes = null, bool analyzeGenerated = false,
        string? closedHierarchyTypes = null, bool reportSuppressed = false, ImmutableArray<DiagnosticAnalyzer>? analyzers = null) =>
        compilation.WithAnalyzers(analyzers ?? Analyzers, new CompilationWithAnalyzersOptions(Options(mustObserveTypes, analyzeGenerated, closedHierarchyTypes),
                onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: reportSuppressed))
            .GetAllDiagnosticsAsync().GetAwaiter().GetResult();

    [GeneratedRegex(@"//\s*expect:\s*(?<ids>[A-Z]+[0-9]+(\s*,\s*[A-Z]+[0-9]+)*)")]
    private static partial Regex ExpectMarker();

    [GeneratedRegex(@"//\s*suppressed:\s*(?<ids>[A-Z]+[0-9]+(\s*,\s*[A-Z]+[0-9]+)*)")]
    private static partial Regex SuppressedMarker();

    public static IReadOnlySet<(string File, int Line, string Id)> Markers(IReadOnlyList<string> sources, bool suppressed = false)
    {
        var markers = new HashSet<(string, int, string)>();
        for (var f = 0; f < sources.Count; f++)
        {
            var lines = sources[f].Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if ((suppressed ? SuppressedMarker() : ExpectMarker()).Match(lines[i]) is { Success: true } m)
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
    public static void Verify(string source, string? mustObserveTypes = null, bool withCore = true, bool generate = false, bool executable = false,
        string? library = null, string? closedHierarchyTypes = null) =>
        Verify([source], mustObserveTypes, withCore, generate, executable, library, closedHierarchyTypes);

    public static void Verify(IReadOnlyList<string> sources, string? mustObserveTypes = null, bool withCore = true, bool generate = false,
        bool executable = false, string? library = null, string? closedHierarchyTypes = null)
    {
        var expected = Markers(sources);
        var ids = expected.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        // Generated shapes are analyzed too: the generator's own code must pass the rules.
        var diagnostics = Diagnose(Compile(sources, withCore, generate, executable, library), mustObserveTypes, analyzeGenerated: generate, closedHierarchyTypes)
            .Where(d => d.Location.IsInSource)
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id.StartsWith("RF", StringComparison.Ordinal) || ids.Contains(d.Id))
            .ToList();
        var actual = diagnostics.Select(Key).ToHashSet();
        var problems = expected.Where(m => !actual.Contains(m)).Select(m => $"expected {m.Id} at {m.File}:{m.Line}, not reported")
            .Concat(diagnostics.Where(d => !expected.Contains(Key(d))).Select(d => $"unexpected {d}"))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// <see cref="Verify(string, string?, bool, bool, bool, string?, string?)"/> with suppressions: a `// suppressed: CS8509`
    /// marker is a diagnostic reported there and suppressed by RFSP0001, an `// expect:` marker one reported and kept,
    /// and no other line has either.
    /// </summary>
    public static void VerifySuppressions(string source, string? library = null, string? dependent = null, string? closedHierarchyTypes = null,
        bool generate = false, string? aliased = null)
    {
        string[] sources = [source];
        var expected = Markers(sources);
        var expectedSuppressed = Markers(sources, suppressed: true);
        var ids = expected.Concat(expectedSuppressed).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var compilation = Compile(sources, generate: generate, library: library, dependent: dependent, aliased: aliased);
        var diagnostics = Diagnose(compilation, closedHierarchyTypes: closedHierarchyTypes, analyzeGenerated: generate, reportSuppressed: true)
            .Where(d => d.Location.IsInSource)
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id.StartsWith("RF", StringComparison.Ordinal) || ids.Contains(d.Id))
            .ToList();
        // A `#pragma warning disable` (the generator's own, for RF0002) is not a suppressor's suppression.
        diagnostics.RemoveAll(d => d.IsSuppressed && !SuppressedBy(d, compilation).Any());
        var kept = diagnostics.Where(d => !d.IsSuppressed).Select(Key).ToHashSet();
        var suppressed = diagnostics.Where(d => d.IsSuppressed).ToList();
        var problems = expected.Where(m => !kept.Contains(m)).Select(m => $"expected {m.Id} at {m.File}:{m.Line}, not reported (or suppressed)")
            .Concat(expectedSuppressed.Where(m => !suppressed.Any(d => Key(d) == m)).Select(m => $"expected {m.Id} suppressed at {m.File}:{m.Line}"))
            .Concat(diagnostics.Where(d => !d.IsSuppressed && !expected.Contains(Key(d))).Select(d => $"unexpected {d}"))
            .Concat(suppressed.Where(d => !expectedSuppressed.Contains(Key(d))).Select(d => $"unexpected suppression of {d}"))
            .Concat(suppressed.Where(d => !SuppressedBy(d, compilation).Contains(Descriptors.ClosedHierarchySwitch.Id)).Select(d => $"{d} suppressed, but not by RFSP0001"))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The IDs of the suppressors that suppressed a diagnostic.</summary>
    public static IEnumerable<string> SuppressedBy(Diagnostic diagnostic, Compilation compilation) =>
        diagnostic.GetSuppressionInfo(compilation)?.ProgrammaticSuppressions.Select(s => s.Descriptor.Id) ?? [];

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
