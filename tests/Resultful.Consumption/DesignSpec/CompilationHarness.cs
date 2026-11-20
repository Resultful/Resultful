using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Resultful.Consumption.DesignSpec;

/// <summary>
/// Lightweight Roslyn CSharpCompilation harness for compile-level design-target assertions.
/// </summary>
internal static class CompilationHarness
{
    public static ImmutableArray<Diagnostic> Compile(
        string source,
        LanguageVersion languageVersion = LanguageVersion.Preview,
        params IIncrementalGenerator[] generators)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var refs = trustedAssemblies
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Resultful.DesignSpec.Probe",
            [tree],
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        if (generators.Length > 0)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators.Select(g => g.AsSourceGenerator()).ToArray(),
                parseOptions: parseOptions);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var genDiagnostics);
            return genDiagnostics.AddRange(updatedCompilation.GetDiagnostics());
        }

        return compilation.GetDiagnostics();
    }

    public static ImmutableArray<Diagnostic> CompileTwoAssemblies(
        string baseAssemblySource,
        string consumerAssemblySource,
        LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var refs = trustedAssemblies
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var baseCompilation = CSharpCompilation.Create(
            "Resultful.DesignSpec.BaseAssembly",
            [CSharpSyntaxTree.ParseText(baseAssemblySource, parseOptions)],
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var peStream = new MemoryStream();
        var emitResult = baseCompilation.Emit(peStream);
        if (!emitResult.Success)
        {
            return emitResult.Diagnostics;
        }

        peStream.Position = 0;
        var baseRef = MetadataReference.CreateFromStream(peStream);
        var consumerRefs = refs.Append(baseRef).ToList();

        var consumerCompilation = CSharpCompilation.Create(
            "Resultful.DesignSpec.ConsumerAssembly",
            [CSharpSyntaxTree.ParseText(consumerAssemblySource, parseOptions)],
            consumerRefs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return consumerCompilation.GetDiagnostics();
    }
}
