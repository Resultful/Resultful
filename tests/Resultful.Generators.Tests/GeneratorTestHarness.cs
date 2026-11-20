using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Resultful.Generators.Tests;

internal sealed record GeneratorRunOutput(
    Compilation InputCompilation,
    Compilation OutputCompilation,
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationDiagnostics,
    IReadOnlyDictionary<string, string> GeneratedSources);

internal static class GeneratorTestHarness
{
    // #37 compiled fixtures against CoreStubSource, a hand-written stand-in core. The fixtures now compile against
    // the target API's core (tests/Resultful.TargetApi/Resultful, throw-null stubs): only compiled, never run, so the
    // emitted code is checked against the members the real core will have. This is a thin adapter over the target
    // stubs, kept here because tests/Resultful.Spec/SpecHarness.cs (04c) wasn't pushed when 13a was drafted; it merges
    // into SpecHarness when 13a is rebased above 04e.
    public static string TargetApiCorePath =>
        Path.Combine(Metadata("TargetApiCoreDir"), "Resultful.dll");

    public static MetadataReference TargetApiCore { get; } = LoadTargetApiCore();

    private static MetadataReference LoadTargetApiCore()
    {
        var path = TargetApiCorePath;
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The target API's core isn't built at {path}; build tests/Resultful.TargetApi/Resultful first.");
        }

        return MetadataReference.CreateFromFile(path);
    }

    private static string Metadata(string key) =>
        typeof(GeneratorTestHarness).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == key).Value!;

    public static string RepoRootDir => Metadata("RepoRootDir");

    public static List<MetadataReference> GetRuntimeReferences()
    {
        // The framework only: not the test's own dependencies (Roslyn, xunit, the generator).
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return trustedAssemblies
            .Where(p => File.Exists(p) && string.Equals(Path.GetDirectoryName(p), runtimeDir, StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    public static CSharpCompilation CreateCompilation(
        IEnumerable<(string FileName, string Source)> sources,
        IEnumerable<MetadataReference>? extraReferences = null,
        string assemblyName = "FixtureAssembly",
        bool includeCore = true,
        OptimizationLevel optimizationLevel = OptimizationLevel.Release,
        LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        var trees = new List<SyntaxTree>();
        foreach (var (name, src) in sources)
        {
            trees.Add(CSharpSyntaxTree.ParseText(src, parseOptions, path: name));
        }

        var refs = GetRuntimeReferences();
        if (includeCore)
        {
            refs.Add(TargetApiCore);
        }

        if (extraReferences is not null)
        {
            refs.AddRange(extraReferences);
        }

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: optimizationLevel,
                nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Warn));
    }

    public static MetadataReference EmitToReference(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errs = string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Failed to emit reference assembly '{compilation.AssemblyName}':\n{errs}");
        }

        ms.Position = 0;
        return MetadataReference.CreateFromStream(ms);
    }

    public static GeneratorRunOutput Run(
        CSharpCompilation compilation,
        out GeneratorDriver driver)
    {
        var generator = new ResultShapeGenerator().AsSourceGenerator();
        driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var genDiags);
        var runResult = driver.GetRunResult();
        var generated = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        return new GeneratorRunOutput(
            compilation,
            outputCompilation,
            runResult,
            genDiags,
            outputCompilation.GetDiagnostics(),
            generated);
    }

    public static byte[] EmitAssemblyBytes(Compilation compilation)
    {
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errs = string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Emit failed:\n{errs}");
        }

        return ms.ToArray();
    }
}
