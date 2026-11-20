using Microsoft.CodeAnalysis;
using Resultful.Spec;
using Xunit;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// The analyzers against the target's contract (tests/Resultful.TargetApi/Resultful.Analyzers/*.tsv): every 16a row is
/// implemented with exactly its ID, severity, title and key, and nothing outside 16a's rows is.
/// </summary>
public sealed class TargetContractTests
{
    static readonly string Dir = Path.Combine(SpecPaths.RepoRoot, "tests", "Resultful.TargetApi", "Resultful.Analyzers");

    static List<Dictionary<string, string>> Rows(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(Dir, file)).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        var header = lines[0].Split('\t');
        return [.. lines.Skip(1).Select(l => l.Split('\t')).Select(cells => header.Select((h, i) => (h, v: i < cells.Length ? cells[i] : "")).ToDictionary(x => x.h, x => x.v))];
    }

    static IEnumerable<DiagnosticDescriptor> Supported => AnalyzerHarness.Analyzers.SelectMany(a => a.SupportedDiagnostics);

    [Fact]
    public void Every_16a_rule_has_the_targets_id_severity_and_title()
    {
        var mine = Rows("diagnostics.tsv").Where(r => r["owner"] == "16a").ToList();
        Assert.Equal(["RF0001", "RF0002"], mine.Select(r => r["id"]));
        foreach (var row in mine)
        {
            var descriptors = Supported.Where(d => d.Id == row["id"]).ToList();
            Assert.NotEmpty(descriptors);
            foreach (var d in descriptors)
            {
                Assert.Equal(row["severity"], d.DefaultSeverity.ToString().ToLowerInvariant());
                Assert.Equal(row["title"], d.Title.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Assert.True(d.IsEnabledByDefault);
            }
        }
    }

    [Fact]
    public void Nothing_outside_16a_is_reported()
    {
        var mine = Rows("diagnostics.tsv").Where(r => r["owner"] == "16a").Select(r => r["id"]).ToHashSet();
        Assert.All(Supported, d => Assert.Contains(d.Id, mine));
    }

    [Fact]
    public void The_editorconfig_keys_are_the_targets()
    {
        var keys = Rows("editorconfig-keys.tsv").Select(r => r["key"]).ToHashSet();
        var mine = Rows("diagnostics.tsv").Where(r => r["owner"] == "16a" && r["editorconfig"].Length > 0).ToList();
        Assert.Equal([Descriptors.MustObserveTypesKey], mine.Select(r => r["editorconfig"]));
        Assert.Equal("RF0001", mine.Single()["id"]);
        Assert.Contains(Descriptors.MustObserveTypesKey, keys);
    }

    [Fact]
    public void Every_16a_code_fix_is_offered_with_fix_all()
    {
        var ids = Supported.Select(d => d.Id).ToHashSet();
        var rows = Rows("code-fixes.tsv").Where(r => ids.Contains(r["id"])).ToList();
        Assert.Equal(["RF0001"], rows.Select(r => r["id"]));
        var row = rows.Single();
        Assert.Contains(row["id"], AnalyzerHarness.Fix.FixableDiagnosticIds);
        Assert.Equal(MustObserveCodeFixProvider.Title, row["fix"], ignoreCase: true);
        Assert.Equal("yes", row["fix_all"]);
        Assert.NotNull(AnalyzerHarness.Fix.GetFixAllProvider());
    }
}
