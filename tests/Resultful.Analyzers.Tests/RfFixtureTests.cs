using Resultful.Spec;
using Xunit;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// The 16a RF-* fixtures in tests/Resultful.Spec, run by 04c's own SpecHarness (linked unchanged) with this analyzer:
/// a compile-only fixture that uses the analyzers compiles against the target API with the real tool, which the
/// harness finds under src/Resultful.Analyzers/bin. Their ledger lines stay `pending` while 16a is drafted ahead.
/// </summary>
public sealed class RfFixtureTests
{
    static readonly Lazy<SpecHarness> Real = new(() => new SpecHarness(Build.Real));

    static Fixture Load(string id) => Fixture.Load(Path.Combine(SpecPaths.FixturesDir, id));

    /// <summary>What 07 puts [MustObserve] on (PKG-CODEANALYSIS, VAL-ZERO), and the target's stubs leave off.</summary>
    public const string CoreMustObserveTypes = "T:Resultful.Result`1, T:Resultful.Result`2, T:Resultful.Validation`1, T:Resultful.Validation`2, T:Resultful.Option`1";

    [Theory]
    [InlineData("RF-0002")]
    [InlineData("RF-0002-UNION")]
    [InlineData("RF-OWN-TYPE")]
    public void Passes_in_04cs_harness_as_written(string id)
    {
        var outcome = Real.Value.Evaluate(Load(id));
        if (id == "RF-0002-UNION" && !AnalyzerHarness.IsNet11)
            Assert.Equal(Status.NotApplicable, outcome.Status);
        else
            Assert.True(outcome.Status == Status.Pass, outcome.Detail);
        TestContext.Current.TestOutputHelper?.WriteLine($"{id} [{outcome.Tfm}]: {outcome.Status} {outcome.Detail}");
    }

    /// <summary>
    /// RF-0001 and RF-0001-ASYNC are about Resultful's own Result&lt;int&gt;, which the target's stub carries no
    /// [MustObserve] for (a spec finding). In 04c's harness they fail only because RF0001 isn't reported for it; this
    /// test pins exactly that, so it fails once the stub gains the attribute and the line can flip.
    /// </summary>
    [Theory]
    [InlineData("RF-0001")]
    [InlineData("RF-0001-ASYNC")]
    public void Fails_in_04cs_harness_only_for_the_missing_attribute_on_the_stub(string id)
    {
        var outcome = Real.Value.Evaluate(Load(id));
        Assert.Equal(Status.Fail, outcome.Status);
        var problems = outcome.Detail.Split('\n').Where(l => l.Length > 0 && !l.StartsWith(' ')).ToList();
        Assert.All(problems, p => Assert.True(
            p.StartsWith("expected RF0001 at ", StringComparison.Ordinal) || p == "expected a code fix for RF0001, but RF0001 isn't reported",
            "a different failure: " + p + "\n" + outcome.Detail));
    }

    /// <summary>The same two fixtures pass when the core's result types are treated as [MustObserve], through .editorconfig.</summary>
    [Theory]
    [InlineData("RF-0001")]
    [InlineData("RF-0001-ASYNC")]
    public void Passes_when_the_cores_types_are_must_observe(string id)
    {
        var fixture = Load(id);
        var sources = fixture.Sources.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Value).ToList();
        AnalyzerHarness.Verify(sources, CoreMustObserveTypes);
        foreach (var fix in fixture.ExpectedFixes)
        {
            Assert.Equal("RF0001", fix);
            var single = Assert.Single(sources);
            var fixedText = AnalyzerHarness.ApplyFix(single, fixAll: true, CoreMustObserveTypes);
            Assert.Empty(AnalyzerHarness.Rf0001(Project(fixedText), CoreMustObserveTypes));
        }
    }

    static Microsoft.CodeAnalysis.Project Project(string source)
    {
        var compilation = AnalyzerHarness.Compile([source]);
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var project = workspace.AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(Microsoft.CodeAnalysis.ProjectId.CreateNewId(), Microsoft.CodeAnalysis.VersionStamp.Default,
            "P", "P", Microsoft.CodeAnalysis.LanguageNames.CSharp, compilationOptions: compilation.Options, parseOptions: AnalyzerHarness.ParseOptions,
            metadataReferences: compilation.References));
        return project.AddDocument("Test0.cs", source).Project;
    }
}
