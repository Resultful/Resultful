using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Resultful.Spec;
using Xunit;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// A hand-rolled harness: Microsoft.CodeAnalysis.Testing's verifiers resolve reference assemblies from NuGet and
/// assume VSTest, while this project runs on Microsoft.Testing.Platform against the target API's stub core and
/// Roslyn 5.12. Consumer code is compiled in process, the analyzers run with an optional .editorconfig, and
/// `// expect: RF0001` markers (the spec's syntax) are compared both ways.
/// </summary>
public static partial class AnalyzerHarness
{
    public static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = [new MustObserveAnalyzer(), new DefaultResultAnalyzer()];

    public static string Tfm => SpecPaths.Tfm;
    public static bool IsNet11 => Tfm == "net11.0";

    static readonly string TargetCore = Path.Combine(SpecPaths.RepoRoot, "tests", "Resultful.TargetApi", "Resultful", "bin", SpecPaths.Configuration, Tfm, "Resultful.dll");

    static readonly Lazy<ImmutableArray<MetadataReference>> FrameworkRefs = new(() =>
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return [.. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => string.Equals(Path.GetDirectoryName(p), runtimeDir, StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    });

    public static CSharpParseOptions ParseOptions => CSharpParseOptions.Default
        .WithLanguageVersion(IsNet11 ? LanguageVersion.Latest : LanguageVersion.CSharp14);

    /// <summary>Compiles consumer sources (Test0.cs, Test1.cs, …) against the stub core, or alone with <paramref name="withCore"/> false.</summary>
    public static CSharpCompilation Compile(IEnumerable<string> sources, bool withCore = true)
    {
        Assert.True(!withCore || File.Exists(TargetCore), $"the target API's core isn't built: {TargetCore}");
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"Test{i}.cs", encoding: Encoding.UTF8));
        var refs = withCore ? FrameworkRefs.Value.Add(MetadataReference.CreateFromFile(TargetCore)) : FrameworkRefs.Value;
        return CSharpCompilation.Create("Consumer", trees, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    public static AnalyzerOptions Options(string? mustObserveTypes) => new([], new ConfigProvider(
        mustObserveTypes is null ? [] : new Dictionary<string, string> { [Descriptors.MustObserveTypesKey] = mustObserveTypes }));

    /// <summary>Compiler and analyzer diagnostics together.</summary>
    public static ImmutableArray<Diagnostic> Diagnose(Compilation compilation, string? mustObserveTypes = null) =>
        compilation.WithAnalyzers(Analyzers, Options(mustObserveTypes)).GetAllDiagnosticsAsync().GetAwaiter().GetResult();

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
    public static void Verify(string source, string? mustObserveTypes = null, bool withCore = true) => Verify([source], mustObserveTypes, withCore);

    public static void Verify(IReadOnlyList<string> sources, string? mustObserveTypes = null, bool withCore = true)
    {
        var expected = Markers(sources);
        var ids = expected.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var diagnostics = Diagnose(Compile(sources, withCore), mustObserveTypes)
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
