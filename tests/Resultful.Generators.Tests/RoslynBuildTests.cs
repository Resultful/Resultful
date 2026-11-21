using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Resultful.Generators.Tests;

// One build per Roslyn version (Known traps): the 4.14 build never emits [Union]; the 5.x build, which these tests run
// in process, decides from the compilation. Each build references the Roslyn of its package folder, no newer.
public sealed class RoslynBuildTests
{
    private static string Roslyn414Path => GeneratorTestHarness.Metadata("Roslyn414Build");

    [Fact]
    public void Each_build_references_the_Roslyn_of_its_package_folder()
    {
        Assert.Equal(new Version(4, 14, 0, 0), CodeAnalysisReference(Roslyn414Path));
        Assert.Equal(new Version(5, 0, 0, 0), CodeAnalysisReference(typeof(ResultShapeGenerator).Assembly.Location));
    }

    // In process the 4.14 build runs on this test's Roslyn 5.12; the SDK 9.0.3xx leg in tests/Compatibility is the
    // check on the real 4.14 compiler. C# 14, so neither build may emit [Union].
    [Theory]
    [InlineData("Flat", FixtureCatalog.FlatHierarchy)]
    [InlineData("Deep", FixtureCatalog.DeepFourLevelHierarchy)]
    [InlineData("ConcreteMid", FixtureCatalog.ConcreteIntermediate)]
    [InlineData("Nested", FixtureCatalog.NestedShape)]
    [InlineData("Valueless", FixtureCatalog.ValuelessShape)]
    [InlineData("Many", FixtureCatalog.ManyErrorsShape)]
    public void Both_builds_emit_byte_identical_source_without_unions(string name, string source)
    {
        var comp = GeneratorTestHarness.CreateCompilation([(name + ".cs", source)], languageVersion: LanguageVersion.CSharp14);
        var old = Sources(comp, LoadRoslyn414());
        var current = Sources(comp, new ResultShapeGenerator());

        Assert.NotEmpty(current);
        Assert.Equal(current.Keys.Order(StringComparer.Ordinal), old.Keys.Order(StringComparer.Ordinal));
        foreach (var (hint, text) in current)
        {
            Assert.True(text == old[hint], $"{hint} differs between the 4.14 and 5.x builds");
        }
    }

    private static Dictionary<string, string> Sources(CSharpCompilation comp, IIncrementalGenerator generator) =>
        CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: (CSharpParseOptions)comp.SyntaxTrees[0].Options)
            .RunGenerators(comp, TestContext.Current.CancellationToken)
            .GetRunResult().Results.SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

    // Microsoft.CodeAnalysis isn't loaded into this context, so the 4.14 build binds to the Roslyn this test runs on.
    private static IIncrementalGenerator LoadRoslyn414()
    {
        var context = new AssemblyLoadContext("Roslyn414", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(Roslyn414Path);
        return (IIncrementalGenerator)Activator.CreateInstance(assembly.GetType("Resultful.Generators.ResultShapeGenerator", throwOnError: true)!)!;
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void The_Roslyn_4_14_build_never_emits_Union_even_where_the_compilation_allows_it()
    {
        var comp = GeneratorTestHarness.CreateCompilation([("Flat.cs", FixtureCatalog.FlatHierarchy)]);
        Assert.NotNull(comp.GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute"));

        var old = LoadRoslyn414();

        var oldShape = Shape(Run(comp, old));
        var newShape = Shape(Run(comp, new ResultShapeGenerator()));

        Assert.DoesNotContain(oldShape.Interfaces, i => i.Name == "IUnion");
        Assert.Empty(oldShape.GetMembers("HasValue"));
        Assert.Contains(newShape.Interfaces, i => i.Name == "IUnion");
    }

    private static Compilation Run(CSharpCompilation comp, IIncrementalGenerator generator)
    {
        CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: (CSharpParseOptions)comp.SyntaxTrees[0].Options)
            .RunGeneratorsAndUpdateCompilation(comp, out var output, out var diagnostics, TestContext.Current.CancellationToken);
        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Error);
        return output;
    }

    private static INamedTypeSymbol Shape(Compilation c) => c.GetTypeByMetadataName("Fixture.Flat.PlaceOrderResult")!;
#endif

    private static Version? CodeAnalysisReference(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences
            .Select(reader.GetAssemblyReference)
            .Where(r => reader.GetString(r.Name) == "Microsoft.CodeAnalysis")
            .Select(r => r.Version)
            .FirstOrDefault();
    }
}
