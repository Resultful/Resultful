using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

// The ledger's generator lines, run here while 13a is drafted ahead: 04c's tests/Resultful.Spec/Fixtures/<ID>/Cases.cs,
// read in place and checked the way SpecHarness checks markers: every `// expect: ID` is reported on its line, nothing
// else is, and an `// expect-clean` file has no warnings. SpecHarness itself runs them too, with
// RESULTFUL_SPEC_GENERATOR set to this generator; this test keeps them in 13a's own suite until 13a's turn, when
// SpecHarness takes over and the test is deleted.
//
// GEN-RFG0005 as written can't pass (CS1729 on the marked line: the generator can't reopen a non-partial Outer to add
// NestedError's constructor). GenFixtures/GenRfg0005Proposed.cs is the fix proposed to 04c, run alongside.
public sealed partial class GenFixtureTests
{
    private static readonly HashSet<string> CheckedWarnings = ["CS8509", "CS8524", "CS8655", "CS0618", "CS0612", "CS8602", "CS8604"];

    // `using Resultful.Spec.Support;` in the fixtures needs SpecSupport.cs's namespace; nothing in it is used here.
    private const string SupportSource = "namespace Resultful.Spec.Support { internal static class Placeholder { } }";

    private static string SpecFixture(string id) => $"tests/Resultful.Spec/Fixtures/{id}/Cases.cs";

    public static TheoryData<string, string> Fixtures() => new()
    {
        { "GEN-CLEAN", SpecFixture("GEN-CLEAN") },
        { "GEN-RFG0001", SpecFixture("GEN-RFG0001") },
        { "GEN-RFG0002", SpecFixture("GEN-RFG0002") },
        { "GEN-RFG0003", SpecFixture("GEN-RFG0003") },
        { "GEN-RFG0004", SpecFixture("GEN-RFG0004") },
        { "GEN-RFG0005 (proposed fix)", "tests/Resultful.Generators.Tests/GenFixtures/GenRfg0005Proposed.cs" },
        { "GEN-RFG0006", SpecFixture("GEN-RFG0006") },
        { "GEN-RFG0008", SpecFixture("GEN-RFG0008") },
        { "UERR-RFG0007", SpecFixture("UERR-RFG0007") },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Ledger_fixture_markers_match(string id, string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(GeneratorTestHarness.RepoRootDir, relativePath));
        var tfms = TfmDirective().Matches(text).SelectMany(m => m.Groups["v"].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
        if (tfms.Count > 0 && !tfms.Contains(CurrentTfm))
        {
            Assert.Skip($"{id} applies to {string.Join(", ", tfms)} only");
        }

        const string FixturePath = "Cases.cs";
        var comp = GeneratorTestHarness.CreateCompilation([(FixturePath, text), ("SpecSupport.cs", SupportSource)], assemblyName: "Fixture_" + new string(id.Where(char.IsLetterOrDigit).ToArray()));
        var output = GeneratorTestHarness.Run(comp, out _);

        var lines = text.Split('\n');
        var expected = new HashSet<(int Line, string Id)>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (ExpectMarker().Match(lines[i]) is { Success: true } m)
            {
                foreach (var markerId in m.Groups["ids"].Value.Split(',', StringSplitOptions.TrimEntries))
                {
                    expected.Add((i + 1, markerId));
                }
            }
        }

        var clean = text.Contains("// expect-clean", StringComparison.Ordinal);
        var markerIds = expected.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var all = output.GeneratorDiagnostics.Concat(output.CompilationDiagnostics);
        var relevant = all
            .Where(d => d.Location.IsInSource && d.Location.SourceTree!.FilePath == FixturePath)
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && (clean || CheckedWarnings.Contains(d.Id) || markerIds.Contains(d.Id) || d.Id.StartsWith("RF", StringComparison.Ordinal))))
            .ToList();
        var actual = relevant.Select(d => (d.Location.GetLineSpan().StartLinePosition.Line + 1, d.Id)).ToHashSet();

        var problems = new List<string>();
        problems.AddRange(expected.Where(e => !actual.Contains(e)).Select(e => $"expected {e.Id} at line {e.Line}, not reported: {lines[e.Line - 1].Trim()}"));
        problems.AddRange(relevant.Where(d => !expected.Contains((d.Location.GetLineSpan().StartLinePosition.Line + 1, d.Id))).Select(d => "unexpected " + d));

        // The generator must never crash (CS8785) on a ledger fixture.
        problems.AddRange(all.Where(d => d.Id is "CS8784" or "CS8785").Select(d => "generator failure: " + d));
        Assert.True(problems.Count == 0, $"{id}:\n" + string.Join("\n", problems));
    }

    private static string CurrentTfm =>
#if NET11_0_OR_GREATER
        "net11.0";
#else
        "net10.0";
#endif

    [GeneratedRegex(@"//\s*expect:\s*(?<ids>[A-Z]+[0-9]+(\s*,\s*[A-Z]+[0-9]+)*)")]
    private static partial Regex ExpectMarker();

    [GeneratedRegex(@"^\s*//\s*spec:\s*tfm\s+(?<v>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex TfmDirective();
}
