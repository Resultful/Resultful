using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The emitted-code gate: every sample's generated code compiles with no compiler warning at any warning level, nullable
/// included, with both outcome styles. The baseline is empty and must stay empty: a warning a consumer would see in code
/// they didn't write is a generator bug. (A consumer's analyzers see generated code as generated, so this is the
/// compiler's own checks, which they can't opt out of.)
/// </summary>
[TestFixture]
public sealed class EmittedCodeTests
{
    public static IEnumerable<TestCaseData> Cases()
        => DerivedEvidenceTests.Samples().Where(s => s != "split").SelectMany(s => new[] { new TestCaseData(s, false), new TestCaseData(s, true) });

    [TestCaseSource(nameof(Cases))]
    public void Generated_code_compiles_without_a_warning(string sample, bool resultful)
    {
        var (spec, overlays) = DerivedEvidenceTests.Paths(sample);
        var api = char.ToUpperInvariant(sample[0]) + sample[1..];
        var (_, _, sources, warnings) = GeneratedServer.BuildWithWarnings(api, spec, overlays, string.Empty, withRules: true, resultful: resultful);

        sources.Should().NotBeEmpty("the gate examined something");

        // The compiler hides nullable warnings in an auto-generated file unless the file turns them on: without this,
        // the gate would pass on code full of them.
        sources.Where(s => !s.Text.Contains("#nullable enable", StringComparison.Ordinal)).Select(s => s.Hint).Should().BeEmpty();
        warnings.Select(w => w.ToString()).Should().BeEmpty();
    }
}
