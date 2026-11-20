using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Resultful.Spec;

// Merged from #36's CompilationHarness (compile a source, or a library and a consumer, with generators) and #37's
// GeneratorTestHarness (emit a compilation as a reference, run the generator driver with step tracking). The stub core
// #37 compiled against is replaced by the target API (tests/Resultful.TargetApi).

/// <summary>Which build a fixture is compiled against.</summary>
public enum Build
{
    /// <summary>The real packages (src/), their generator and analyzers when they exist. Decides the outcome.</summary>
    Real,

    /// <summary>The target API's throw-null stubs. Compile only: proves the fixture is written against the target.</summary>
    Target,
}

public enum Status { Pass, Fail, NotApplicable }

public sealed record Outcome(string Id, string Tfm, Status Status, string Detail)
{
    public bool Passed => Status == Status.Pass;
}

/// <summary>An expected diagnostic: an ID on a line of a file (`// expect: CS8509`).</summary>
public sealed record Marker(string File, int Line, string Id, string SourceLine);

/// <summary>One fixture directory, Fixtures/&lt;ID&gt;/, with the directives and markers parsed from its files.</summary>
public sealed class Fixture
{
    static readonly Regex Directive = new(@"^\s*//\s*spec:\s*(?<key>\w[\w-]*)\s+(?<value>.+?)\s*$", RegexOptions.Compiled);
    static readonly Regex Expect = new(@"//\s*expect:\s*(?<ids>[A-Z]+[0-9]+(\s*,\s*[A-Z]+[0-9]+)*)", RegexOptions.Compiled);
    static readonly Regex ExpectFix = new(@"//\s*expect-fix:\s*(?<id>[A-Z]+[0-9]+)", RegexOptions.Compiled);

    public required string Id { get; init; }
    public required string Directory { get; init; }

    /// <summary>Main sources, relative path to text. Files under lib/ and lib.v2/ are separate assemblies.</summary>
    public required IReadOnlyDictionary<string, string> Sources { get; init; }
    public required IReadOnlyDictionary<string, string> LibSources { get; init; }
    public required IReadOnlyDictionary<string, string> LibV2Sources { get; init; }

    /// <summary>`// spec: tfm net11.0`: the only target frameworks the fixture applies to (empty means all).</summary>
    public required IReadOnlySet<string> Tfms { get; init; }

    /// <summary>`// spec: uses X`: packages (Resultful.Text.Json, …) and tools (generator, analyzers, aspnetcore, stj-generator).</summary>
    public required IReadOnlySet<string> Uses { get; init; }

    public required IReadOnlyList<Marker> Markers { get; init; }
    public required IReadOnlyList<string> ExpectedFixes { get; init; }
    public required IReadOnlySet<string> CleanFiles { get; init; }

    public bool HasMarkers => Markers.Count > 0 || CleanFiles.Count > 0;

    public static Fixture Load(string directory)
    {
        var id = Path.GetFileName(directory);
        Dictionary<string, string> main = [], lib = [], libV2 = [];
        foreach (var path in System.IO.Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(directory, path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            if (rel.StartsWith("lib.v2/", StringComparison.Ordinal)) libV2[rel] = text;
            else if (rel.StartsWith("lib/", StringComparison.Ordinal)) lib[rel] = text;
            else main[rel] = text;
        }

        HashSet<string> tfms = [], uses = [], clean = [];
        List<Marker> markers = [];
        List<string> fixes = [];
        foreach (var (rel, text) in main.Concat(lib).Concat(libV2))
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (Directive.Match(line) is { Success: true } d)
                {
                    var values = d.Groups["value"].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                    switch (d.Groups["key"].Value)
                    {
                        case "tfm": tfms.UnionWith(values); break;
                        case "uses": uses.UnionWith(values); break;
                        default: throw new InvalidOperationException($"{id}/{rel}:{i + 1}: unknown directive `{line.Trim()}`");
                    }
                }

                if (line.Contains("// expect-clean", StringComparison.Ordinal)) clean.Add(rel);
                if (ExpectFix.Match(line) is { Success: true } f) fixes.Add(f.Groups["id"].Value);
                else if (Expect.Match(line) is { Success: true } e)
                {
                    foreach (var mid in e.Groups["ids"].Value.Split(',', StringSplitOptions.TrimEntries))
                        markers.Add(new Marker(rel, i + 1, mid, line.Trim()));
                }
            }
        }

        return new Fixture
        {
            Id = id, Directory = directory, Sources = main, LibSources = lib, LibV2Sources = libV2,
            Tfms = tfms, Uses = uses, Markers = markers, ExpectedFixes = fixes, CleanFiles = clean,
        };
    }
}

/// <summary>Compiles, checks and runs fixtures. One instance per build and target framework.</summary>
public sealed class SpecHarness
{
    // Warnings that are part of what fixtures assert on, so they are compared both ways like errors.
    static readonly HashSet<string> CheckedWarnings = ["CS8509", "CS8524", "CS8655", "CS0618", "CS0612", "CS8602", "CS8604"];

    readonly Build _build;
    readonly string _tfm;
    readonly List<MetadataReference> _frameworkRefs;
    readonly Lazy<IReadOnlyList<MetadataReference>> _aspNetRefs;
    readonly Dictionary<string, string> _packageDlls;
    readonly Lazy<Dictionary<string, string>> _targetPackageDlls;
    readonly Lazy<(ImmutableArray<ISourceGenerator> Generators, ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<CodeFixProvider> Fixes, string[] Missing)> _tools;

    public SpecHarness(Build build, string? tfm = null)
    {
        _build = build;
        _tfm = tfm ?? SpecPaths.Tfm;
        _frameworkRefs = FrameworkReferences();
        _aspNetRefs = new(() => AspNetReferences());
        _targetPackageDlls = new(TargetPackages);
        _packageDlls = build == Build.Real ? RealPackages() : _targetPackageDlls.Value;
        _tools = new(LoadTools);
    }

    public Build Build => _build;
    public string Tfm => _tfm;

    public CSharpParseOptions ParseOptions => CSharpParseOptions.Default
        .WithLanguageVersion(_tfm == "net11.0" ? LanguageVersion.Latest : LanguageVersion.CSharp14)
        .WithPreprocessorSymbols(_tfm == "net11.0" ? Net11Symbols : Net10Symbols);

    static readonly string[] Net10Symbols = ["NET", "NET10_0_OR_GREATER", "NET10_0"];
    static readonly string[] Net11Symbols = ["NET", "NET10_0_OR_GREATER", "NET11_0_OR_GREATER", "NET11_0"];

    /// <summary>
    /// A compile-only fixture (no [SpecCase]) whose subject is a tool compiles against the target API with the real tool,
    /// so the generator's and the analyzers' lines can flip before the real core has every member (13a flips GEN-*).
    /// </summary>
    public static bool ToolOnly(Fixture fixture) =>
        (fixture.Uses.Contains("generator") || fixture.Uses.Contains("analyzers"))
        && !fixture.Sources.Values.Concat(fixture.LibSources.Values).Any(s => s.Contains("[SpecCase]", StringComparison.Ordinal));

    Dictionary<string, string> PackagesFor(Fixture fixture) => _build == Build.Real && ToolOnly(fixture) ? _targetPackageDlls.Value : _packageDlls;

    /// <summary>Packages and tools the fixture uses that this build doesn't have yet.</summary>
    public IReadOnlyList<string> MissingFor(Fixture fixture)
    {
        var missing = new List<string>();
        var packages = PackagesFor(fixture);
        foreach (var use in fixture.Uses.Order(StringComparer.Ordinal))
        {
            if (use.StartsWith("Resultful.", StringComparison.Ordinal) && !packages.ContainsKey(use)) missing.Add(use);
            if (_build == Build.Real && use is "generator" or "analyzers" && _tools.Value.Missing.Contains(use)) missing.Add("Resultful." + (use == "generator" ? "Generators" : "Analyzers"));
            if (_build == Build.Real && use == "core-union" && !CoreIsUnion()) missing.Add("the core's union members (decision 3, 24)");
        }

        if (!packages.ContainsKey("Resultful")) missing.Add("Resultful");
        return missing;
    }

    bool CoreIsUnion()
    {
        var compilation = CSharpCompilation.Create("probe", [], References(new HashSet<string>(), _packageDlls));
        return compilation.GetTypeByMetadataName("Resultful.Result`2")?.AllInterfaces.Any(i => i.ToDisplayString() == "System.Runtime.CompilerServices.IUnion") == true;
    }

    public Outcome Evaluate(Fixture fixture)
    {
        if (fixture.Tfms.Count > 0 && !fixture.Tfms.Contains(_tfm))
            return new(fixture.Id, _tfm, Status.NotApplicable, $"applies to {string.Join(", ", fixture.Tfms)} only");
        try
        {
            return _build == Build.Real ? EvaluateReal(fixture) : EvaluateAgainstTarget(fixture);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(fixture.Id, _tfm, Status.Fail, "harness error: " + ex);
        }
    }

    Outcome EvaluateReal(Fixture fixture)
    {
        var missing = MissingFor(fixture);
        if (missing.Count > 0) return new(fixture.Id, _tfm, Status.Fail, "not built yet: " + string.Join(", ", missing));

        var compiled = CompileFixture(fixture, fixture.LibSources);
        if (compiled.Error is { } libError) return new(fixture.Id, _tfm, Status.Fail, libError);

        var problems = new List<string>();
        problems.AddRange(CheckMarkers(fixture, compiled.Diagnostics));
        problems.AddRange(CheckFixes(fixture, compiled.Compilation));

        var hasErrors = compiled.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        if (!fixture.HasMarkers && hasErrors)
            problems.Add("doesn't compile:\n" + Describe(compiled.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), fixture));

        var cases = 0;
        if (!hasErrors && problems.Count == 0)
        {
            var libForRun = fixture.LibV2Sources.Count > 0 ? CompileLibrary(fixture, fixture.LibV2Sources) : compiled.Library;
            if (libForRun.Error is { } v2Error) return new(fixture.Id, _tfm, Status.Fail, v2Error);
            var run = Run(compiled.Compilation, libForRun.Bytes);
            cases = run.Cases;
            problems.AddRange(run.Problems);
        }

        if (!fixture.HasMarkers && fixture.ExpectedFixes.Count == 0 && cases == 0 && problems.Count == 0)
            problems.Add("the fixture has no markers and no [SpecCase] methods, so it checks nothing");

        return problems.Count == 0
            ? new(fixture.Id, _tfm, Status.Pass, cases > 0 ? $"{cases} case(s) passed" : "markers matched")
            : new(fixture.Id, _tfm, Status.Fail, string.Join("\n", problems));
    }

    /// <summary>
    /// Against the target API: every error must be one the fixture expects. Diagnostics only a generator or analyzer
    /// can produce are not required, and fixtures that need generated code are not applicable.
    /// </summary>
    Outcome EvaluateAgainstTarget(Fixture fixture)
    {
        if (fixture.Uses.Contains("generator") && CandidateGenerator is null)
            return new(fixture.Id, _tfm, Status.NotApplicable, "needs generated code; checked against the real generator (or set " + CandidateGeneratorVariable + ")");
        if (fixture.Uses.Contains("core-union"))
            return new(fixture.Id, _tfm, Status.NotApplicable, "the core's union members are chosen by measurement (decision 3) and added to the target in 24");
        var missing = MissingFor(fixture);
        if (missing.Count > 0) return new(fixture.Id, _tfm, Status.Fail, "the target has no " + string.Join(", ", missing));

        var compiled = CompileFixture(fixture, fixture.LibSources);
        if (compiled.Error is { } libError) return new(fixture.Id, _tfm, Status.Fail, libError);
        // Compiler errors the fixture expects are allowed; RF and RFG markers need tools the target doesn't have.
        var expected = fixture.Markers.Where(m => m.Id.StartsWith("CS", StringComparison.Ordinal) || CandidateGenerator is not null && m.Id.StartsWith("RFG", StringComparison.Ordinal))
            .Select(m => (m.File, m.Line, m.Id)).ToHashSet();
        var errors = compiled.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => !expected.Contains(Key(d))).ToList();
        // And every compiler diagnostic the fixture expects is what the target API gives (a suppressor's subject aside).
        var reported = compiled.Diagnostics.Select(Key).ToHashSet();
        var absent = fixture.Markers.Where(m => m.Id.StartsWith("CS", StringComparison.Ordinal) && !reported.Contains((m.File, m.Line, m.Id))).ToList();
        if (errors.Count == 0 && absent.Count == 0) return new(fixture.Id, _tfm, Status.Pass, "compiles against the target API");
        return new(fixture.Id, _tfm, Status.Fail, string.Join("\n",
            errors.Count > 0 ? "doesn't compile against the target API:\n" + Describe(errors, fixture) : "",
            string.Join("\n", absent.Select(m => $"expects {m.Id} at {m.File}:{m.Line}, which the target API doesn't give\n    {m.SourceLine}"))).Trim());
    }

    // ---- compiling ----

    public sealed record LibraryBuild(byte[]? Bytes, MetadataReference? Reference, string? Error);

    sealed record FixtureBuild(Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics, LibraryBuild Library, string? Error);

    FixtureBuild CompileFixture(Fixture fixture, IReadOnlyDictionary<string, string> libSources)
    {
        var library = libSources.Count > 0 ? CompileLibrary(fixture, libSources) : new LibraryBuild(null, null, null);
        if (library.Error is not null)
            return new(CSharpCompilation.Create("empty"), [], library, library.Error);
        var extra = library.Reference is null ? [] : new[] { library.Reference };
        var (compilation, diagnostics) = CompileWithTools(fixture, fixture.Sources, "Fixture." + fixture.Id, extra, PackagesFor(fixture));
        return new(compilation, diagnostics, library, null);
    }

    LibraryBuild CompileLibrary(Fixture fixture, IReadOnlyDictionary<string, string> sources)
    {
        var (compilation, diagnostics) = CompileWithTools(fixture, sources, "FixtureLib", [], PackagesFor(fixture));
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0) return new(null, null, "the fixture's library doesn't compile:\n" + Describe(errors, fixture));
        var bytes = Emit(compilation);
        return new(bytes, MetadataReference.CreateFromImage(bytes), null);
    }

    /// <summary>Compiles sources with the support file, runs the generator, then the analyzers (and suppressors).</summary>
    public (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) CompileWithTools(
        Fixture? fixture, IReadOnlyDictionary<string, string> sources, string assemblyName, IEnumerable<MetadataReference> extraReferences,
        IReadOnlyDictionary<string, string>? packages = null)
    {
        var uses = fixture?.Uses ?? new HashSet<string>();
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s.Value, ParseOptions, path: s.Key, encoding: Encoding.UTF8)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText(SpecPaths.SupportSource, ParseOptions, path: "SpecSupport.cs"));
        var compilation = (Compilation)CSharpCompilation.Create(assemblyName, trees, References(uses, packages ?? _packageDlls).Concat(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Release, generalDiagnosticOption: ReportDiagnostic.Default));

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var generators = new List<ISourceGenerator>();
        if (uses.Contains("generator") && (_build == Build.Real || CandidateGenerator is not null)) generators.AddRange(_tools.Value.Generators);
        if (uses.Contains("stj-generator")) generators.AddRange(StjGenerator());
        if (generators.Count > 0)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: ParseOptions,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
            driver.RunGeneratorsAndUpdateCompilation(compilation, out compilation, out var generatorDiagnostics);
            diagnostics.AddRange(generatorDiagnostics);
        }

        if (_build == Build.Real && uses.Contains("analyzers") && _tools.Value.Analyzers.Length > 0)
        {
            var withAnalyzers = compilation.WithAnalyzers(_tools.Value.Analyzers,
                new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, concurrentAnalysis: false, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true));
            diagnostics.AddRange(withAnalyzers.GetAllDiagnosticsAsync().GetAwaiter().GetResult().Where(d => !d.IsSuppressed));
        }
        else
        {
            diagnostics.AddRange(compilation.GetDiagnostics());
        }

        return (compilation, diagnostics.ToImmutable());
    }

    IEnumerable<MetadataReference> References(IReadOnlySet<string> uses, IReadOnlyDictionary<string, string> packages)
    {
        var refs = new List<MetadataReference>(_frameworkRefs);
        if (uses.Contains("aspnetcore") || uses.Contains("Resultful.AspNetCore") || uses.Contains("Resultful.Diagnostics")) refs.AddRange(_aspNetRefs.Value);
        foreach (var (name, path) in packages)
        {
            // The core is always referenced; a satellite and its third-party dependencies only when the fixture uses it.
            if (name == "Resultful" || uses.Contains(name) || ThirdParty.TryGetValue(name, out var owner) && uses.Contains(owner))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        return refs;
    }

    // Third-party assemblies, keyed by the package that needs them. The 0.2 core needs OneOf.
    static readonly Dictionary<string, string> ThirdParty = new(StringComparer.Ordinal)
    {
        ["OneOf"] = "Resultful.OneOf",
        ["FluentValidation"] = "Resultful.FluentValidation",
    };

    // ---- markers ----

    static (string File, int Line, string Id) Key(Diagnostic d)
    {
        if (!d.Location.IsInSource) return ("(no file)", 0, d.Id);
        var span = d.Location.GetMappedLineSpan();
        return ((span.Path ?? "").Replace('\\', '/'), span.StartLinePosition.Line + 1, d.Id);
    }

    IEnumerable<string> CheckMarkers(Fixture fixture, ImmutableArray<Diagnostic> diagnostics)
    {
        if (!fixture.HasMarkers) yield break;
        var markerIds = fixture.Markers.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var relevant = diagnostics.Where(d => d.Location.IsInSource && fixture.Sources.ContainsKey(d.Location.SourceTree!.FilePath))
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || d.Severity == DiagnosticSeverity.Warning && (CheckedWarnings.Contains(d.Id) || markerIds.Contains(d.Id) || d.Id.StartsWith("RF", StringComparison.Ordinal))
                || fixture.CleanFiles.Contains(d.Location.SourceTree!.FilePath) && d.Severity >= DiagnosticSeverity.Warning)
            .ToList();
        var actual = relevant.Select(Key).ToHashSet();
        var expected = fixture.Markers.Select(m => (m.File, m.Line, m.Id)).ToHashSet();

        foreach (var m in fixture.Markers.Where(m => !actual.Contains((m.File, m.Line, m.Id))))
            yield return $"expected {m.Id} at {m.File}:{m.Line}, not reported\n    {m.SourceLine}"
                + (relevant.Where(d => Key(d).Line == m.Line && Key(d).File == m.File).Select(d => $"\n    (got {d.Id} there: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)})").FirstOrDefault() ?? "");
        foreach (var d in relevant.Where(d => !expected.Contains(Key(d))))
            yield return "unexpected " + Describe([d], fixture);
    }

    // ---- code fixes ----

    IEnumerable<string> CheckFixes(Fixture fixture, Compilation compilation)
    {
        if (fixture.ExpectedFixes.Count == 0) yield break;
        foreach (var id in fixture.ExpectedFixes)
        {
            var provider = _tools.Value.Fixes.FirstOrDefault(p => p.FixableDiagnosticIds.Contains(id));
            if (provider is null) { yield return $"expected a code fix for {id}: no CodeFixProvider in the analyzers fixes it"; continue; }
            var error = ApplyFix(fixture, compilation, provider, id);
            if (error is not null) yield return error;
        }
    }

    string? ApplyFix(Fixture fixture, Compilation compilation, CodeFixProvider provider, string id)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "FixProject", "FixProject", LanguageNames.CSharp,
            compilationOptions: compilation.Options, parseOptions: ParseOptions, metadataReferences: compilation.References));
        foreach (var tree in compilation.SyntaxTrees)
            project = project.AddDocument(tree.FilePath, tree.GetText(), filePath: tree.FilePath).Project;

        var before = Diagnose(project).Where(d => d.Id == id).ToList();
        if (before.Count == 0) return $"expected a code fix for {id}, but {id} isn't reported";
        var diagnostic = before[0];
        var document = project.GetDocument(diagnostic.Location.SourceTree!)!;
        var actions = new List<CodeAction>();
        provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None)).GetAwaiter().GetResult();
        if (actions.Count == 0) return $"the code fix for {id} offered no action";
        var operations = actions[0].GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult();
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetProject(project.Id)!;
        var after = Diagnose(changed).Where(d => d.Id == id).ToList();
        var errors = changed.GetCompilationAsync().GetAwaiter().GetResult()!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (after.Count >= before.Count) return $"the code fix for {id} left the diagnostic in place";
        return errors.Count > 0 ? $"the code fix for {id} broke the build:\n" + Describe(errors, fixture) : null;

        IEnumerable<Diagnostic> Diagnose(Project p) =>
            p.GetCompilationAsync().GetAwaiter().GetResult()!.WithAnalyzers(_tools.Value.Analyzers).GetAllDiagnosticsAsync().GetAwaiter().GetResult();
    }

    // ---- running ----

    sealed record RunResult(int Cases, List<string> Problems);

    static RunResult Run(Compilation compilation, byte[]? library)
    {
        var bytes = Emit(compilation);
        var context = new FixtureLoadContext(library);
        var problems = new List<string>();
        var cases = 0;
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(bytes));
            var methods = assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "SpecCaseAttribute"))
                .OrderBy(m => m.DeclaringType!.FullName + "." + m.Name, StringComparer.Ordinal)
                .ToList();
            foreach (var method in methods)
            {
                cases++;
                var name = $"{method.DeclaringType!.Name}.{method.Name}";
                if (method.GetParameters().Length > 0) { problems.Add($"{name}: a [SpecCase] takes no parameters"); continue; }
                try
                {
                    var result = method.Invoke(null, null);
                    if (result is Task task) task.GetAwaiter().GetResult();
                    else if (result is ValueTask valueTask) valueTask.AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
                    problems.Add($"{name}: {inner.GetType().Name}: {inner.Message}");
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            problems.Add("the fixture assembly didn't load: " + string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message)));
        }
        finally
        {
            context.Unload();
        }

        return new(cases, problems);
    }

    static byte[] Emit(Compilation compilation)
    {
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
            throw new InvalidOperationException("emit failed:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return ms.ToArray();
    }

    /// <summary>Loads a fixture and its library in isolation; Resultful and friends come from the test's output folder.</summary>
    sealed class FixtureLoadContext(byte[]? library) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == "FixtureLib" && library is not null) return LoadFromStream(new MemoryStream(library));
            var local = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            if (File.Exists(local) && name.Name!.StartsWith("Resultful", StringComparison.Ordinal)) return LoadFromAssemblyPath(local);
            var aspnet = SpecPaths.AspNetDirectory is { } dir ? Path.Combine(dir, name.Name + ".dll") : null;
            if (aspnet is not null && File.Exists(aspnet) && Default.Assemblies.All(a => a.GetName().Name != name.Name)) return LoadFromAssemblyPath(aspnet);
            return null;
        }
    }

    // ---- references and tools ----

    static List<MetadataReference> FrameworkReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => string.Equals(Path.GetDirectoryName(p), runtimeDir, StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    static List<MetadataReference> AspNetReferences() => SpecPaths.AspNetDirectory is { } dir
        ? Directory.GetFiles(dir, "*.dll").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList()
        : [];

    /// <summary>The real packages: whatever Resultful*.dll the test's output holds, plus third-party dependencies.</summary>
    static Dictionary<string, string> RealPackages()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("Resultful", StringComparison.Ordinal) && name != "Resultful.Spec" || ThirdParty.ContainsKey(name)) map[name] = path;
        }

        return map;
    }

    /// <summary>The target API's stubs, built by the ProjectReferences with ReferenceOutputAssembly=false.</summary>
    Dictionary<string, string> TargetPackages()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = Path.Combine(SpecPaths.RepoRoot, "tests", "Resultful.TargetApi");
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            var tfmDir = Path.Combine(dir, "bin", SpecPaths.Configuration);
            var path = new[] { _tfm, "netstandard2.0" }.Select(t => Path.Combine(tfmDir, t, name + ".dll")).FirstOrDefault(File.Exists);
            if (path is not null) map[name] = path;
        }

        foreach (var (name, _) in ThirdParty)
        {
            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (File.Exists(path)) map[name] = path;
        }

        return map;
    }

    /// <summary>The real generator and analyzers, from their build output under src/, when they exist.</summary>
    (ImmutableArray<ISourceGenerator>, ImmutableArray<DiagnosticAnalyzer>, ImmutableArray<CodeFixProvider>, string[]) LoadTools()
    {
        var missing = new List<string>();
        var generators = ImmutableArray<ISourceGenerator>.Empty;
        var analyzers = ImmutableArray<DiagnosticAnalyzer>.Empty;
        var fixes = ImmutableArray<CodeFixProvider>.Empty;
        if (_build != Build.Real)
        {
            if (CandidateGenerator is { } candidate) generators = new AnalyzerFileReference(candidate, Loader.Instance).GetGenerators(LanguageNames.CSharp);
            return (generators, analyzers, fixes, ["generator", "analyzers"]);
        }

        if (FindTool("Resultful.Generators") is { } g) generators = new AnalyzerFileReference(g, Loader.Instance).GetGenerators(LanguageNames.CSharp);
        else missing.Add("generator");
        if (FindTool("Resultful.Analyzers") is { } a)
        {
            var reference = new AnalyzerFileReference(a, Loader.Instance);
            analyzers = reference.GetAnalyzers(LanguageNames.CSharp);
            fixes = [.. Loader.Instance.LoadFromPath(a).GetTypes()
                .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
                .Select(t => (CodeFixProvider)Activator.CreateInstance(t)!)];
        }
        else missing.Add("analyzers");
        return (generators, analyzers, fixes, missing.ToArray());
    }

    /// <summary>
    /// A generator to try against the target API (a path to Resultful.Generators.dll), so generator fixtures can be
    /// compile-checked before the real one exists, e.g. while building 13a. Unset, they are not applicable to the target.
    /// </summary>
    public const string CandidateGeneratorVariable = "RESULTFUL_SPEC_GENERATOR";

    static string? CandidateGenerator => Environment.GetEnvironmentVariable(CandidateGeneratorVariable) is { Length: > 0 } p && File.Exists(p) ? p : null;

    static string? FindTool(string name)
    {
        var src = Path.Combine(SpecPaths.RepoRoot, "src");
        return Directory.Exists(src)
            ? Directory.GetDirectories(src, name + "*")
                .SelectMany(d => Directory.Exists(Path.Combine(d, "bin")) ? Directory.GetFiles(Path.Combine(d, "bin"), name + ".dll", SearchOption.AllDirectories) : [])
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    /// <summary>System.Text.Json's source generator from the targeting pack, for AOT-style fixtures.</summary>
    static IEnumerable<ISourceGenerator> StjGenerator()
    {
        var path = SpecPaths.StjGeneratorPath ?? throw new InvalidOperationException("System.Text.Json.SourceGeneration.dll not found in the targeting pack");
        return new AnalyzerFileReference(path, Loader.Instance).GetGenerators(LanguageNames.CSharp);
    }

    sealed class Loader : IAnalyzerAssemblyLoader
    {
        public static readonly Loader Instance = new();
        public void AddDependencyLocation(string fullPath) { }
        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }

    public static string Describe(IEnumerable<Diagnostic> diagnostics, Fixture? fixture) => string.Join("\n", diagnostics.Select(d =>
    {
        var (file, line, id) = Key(d);
        var source = fixture is not null && fixture.Sources.TryGetValue(file, out var text) ? text.Split('\n').ElementAtOrDefault(line - 1)?.Trim() : null;
        return $"    {file}:{line}: {d.Severity.ToString().ToLowerInvariant()} {id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"
            + (source is null ? "" : $"\n        {source}");
    }));
}

/// <summary>Where things are, read from the assembly metadata the project writes.</summary>
public static class SpecPaths
{
    static string Meta(string key) => typeof(SpecPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;

    public static string SpecDir => Meta("SpecDir");
    public static string RepoRoot => Meta("RepoRoot");
    public static string Configuration => Meta("Configuration");
    public static string Tfm => Meta("TargetFramework");
    public static string FixturesDir => Path.Combine(SpecDir, "Fixtures");
    public static string LedgerPath => Path.Combine(SpecDir, "ledger.tsv");
    public static string SupportSource => File.ReadAllText(Path.Combine(SpecDir, "Support", "SpecSupport.cs"));

    static string DotnetRoot => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));

    /// <summary>The ASP.NET Core shared framework matching the running runtime's major version.</summary>
    public static string? AspNetDirectory { get; } = FindFramework("Microsoft.AspNetCore.App");

    static string? FindFramework(string name)
    {
        var dir = Path.Combine(DotnetRoot, "shared", name);
        var major = Environment.Version.Major + ".";
        return Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Where(d => Path.GetFileName(d).StartsWith(major, StringComparison.Ordinal)).OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    public static string? StjGeneratorPath { get; } = FindStjGenerator();

    static string? FindStjGenerator()
    {
        var packs = Path.Combine(DotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var major = Environment.Version.Major + ".";
        var pack = Directory.Exists(packs)
            ? Directory.GetDirectories(packs).Where(d => Path.GetFileName(d).StartsWith(major, StringComparison.Ordinal)).OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault()
            : null;
        return pack is null
            ? null
            : Directory.GetFiles(Path.Combine(pack, "analyzers"), "System.Text.Json.SourceGeneration.dll", SearchOption.AllDirectories)
                .OrderByDescending(p => p, StringComparer.Ordinal).FirstOrDefault();
    }
}
