using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>The rules' contract (IDs, severities, keys, fixes) and the Roslyn 4.14 build, loaded by path as csc loads it.</summary>
public sealed class RoslynBuildTests
{
    static readonly string[] AnalyzerTypes =
    [
        "MustObserveAnalyzer", "DefaultResultAnalyzer", "UncheckedValueAnalyzer", "BareStringAnalyzer", "UnassignedFieldAnalyzer",
        "UntypedHandlerAnalyzer", "MapToBindAnalyzer",
    ];

    // diagnostics.tsv and code-fixes.tsv of the 0.3 analyzer contract.
    static readonly Dictionary<string, DiagnosticSeverity> Severities = new()
    {
        ["RF0001"] = DiagnosticSeverity.Warning,
        ["RF0002"] = DiagnosticSeverity.Warning,
        ["RF0003"] = DiagnosticSeverity.Warning,
        ["RF0004"] = DiagnosticSeverity.Info,
        ["RF0006"] = DiagnosticSeverity.Warning,
        ["RF0008"] = DiagnosticSeverity.Error,
        ["RF0009"] = DiagnosticSeverity.Warning,
    };

    static readonly (CodeFixProvider Fix, string Id, bool FixAll)[] Fixes =
    [
        (new MustObserveCodeFixProvider(), "RF0001", true), (new BareStringCodeFixProvider(), "RF0004", false),
        (new UntypedHandlerCodeFixProvider(), "RF0008", true), (new MapToBindCodeFixProvider(), "RF0009", true),
    ];

    [Fact]
    public void The_rules_have_their_ids_severities_and_key()
    {
        var supported = AnalyzerHarness.Analyzers.SelectMany(a => a.SupportedDiagnostics).ToList();
        Assert.Equal(Severities.Keys.Order(StringComparer.Ordinal), supported.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal));
        Assert.All(supported, d =>
        {
            Assert.Equal(Severities[d.Id], d.DefaultSeverity);
            Assert.True(d.IsEnabledByDefault);
            Assert.Equal(Descriptors.Category, d.Category);
        });
        Assert.All(supported.Where(d => d.Id == "RF0001"), d => Assert.Equal(Descriptors.MustObserveTitle, d.Title.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal("resultful.must_observe_types", Descriptors.MustObserveTypesKey);
        Assert.All(Fixes, f =>
        {
            Assert.Equal([f.Id], f.Fix.FixableDiagnosticIds);
            Assert.Equal(f.FixAll, f.Fix.GetFixAllProvider() is not null);
        });
    }

    [Fact]
    public void Each_build_references_the_Roslyn_of_its_package_folder()
    {
        Assert.Equal(new Version(4, 14, 0, 0), Reference(Metadata("Roslyn414Build"), "Microsoft.CodeAnalysis"));
        Assert.Equal(new Version(5, 0, 0, 0), Reference(typeof(MustObserveAnalyzer).Assembly.Location, "Microsoft.CodeAnalysis"));
    }

    // In process the 4.14 build runs on this test's Roslyn 5.12; the SDK 9.0.3xx leg in tests/Compatibility is the
    // check on the real 4.14 compiler.
    [Fact]
    public void The_414_build_reports_what_the_5x_build_reports()
    {
        const string Source = """
            using Resultful;

            public static class Cases
            {
                static Result<int> Get() => Result.Ok(1);
                static void Forgets() { Get(); }
                static Result<int> Zero() => default;
                static int Unchecked() => Get().Value;
                static Result<string> Bare() => "not found";
                static Result<Result<int>> Nested() => Get().Map(x => Get());
                static Result<int> _never;
            }
            """;
        var compilation = Compile([Source]);
        var context = new AssemblyLoadContext("Roslyn414", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(Metadata("Roslyn414Build"));
        ImmutableArray<DiagnosticAnalyzer> old = [.. AnalyzerTypes.Select(n => (DiagnosticAnalyzer)Activator.CreateInstance(assembly.GetType("Resultful.Analyzers." + n, throwOnError: true)!)!)];
        Assert.Equal(Ids(compilation, AnalyzerHarness.Analyzers), Ids(compilation, old));
        Assert.Equal(["RF0001", "RF0002", "RF0003", "RF0004", "RF0006", "RF0009"], Ids(compilation, old));
    }

    static List<string> Ids(Compilation compilation, ImmutableArray<DiagnosticAnalyzer> analyzers) =>
        [.. compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult()
            .Select(d => d.Id).Order(StringComparer.Ordinal)];

    static Version? Reference(string path, string name)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences.Select(reader.GetAssemblyReference)
            .Where(r => reader.GetString(r.Name) == name).Select(r => r.Version).FirstOrDefault();
    }
}
