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

    [Theory]
    [InlineData("RF-0001")]
    [InlineData("RF-0001-ASYNC")]
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
}
