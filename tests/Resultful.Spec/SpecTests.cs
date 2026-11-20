using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

[assembly: AssemblyFixture(typeof(Resultful.Spec.SpecReport))]

namespace Resultful.Spec;

/// <summary>Collects each fixture's outcome and writes artifacts/spec-report.json when the run ends.</summary>
public sealed class SpecReport : IDisposable
{
    public static readonly ConcurrentDictionary<string, Outcome> Outcomes = new(StringComparer.Ordinal);

    public void Dispose()
    {
        if (Outcomes.IsEmpty) return;
        var ledger = Ledger.Parse(File.ReadAllLines(SpecPaths.LedgerPath), []).ToDictionary(l => l.Id, StringComparer.Ordinal);
        var dir = Path.Combine(SpecPaths.RepoRoot, "artifacts", "spec");
        Directory.CreateDirectory(dir);

        // One file per target framework, then a merged spec-report.json with an outcome per target.
        var mine = Outcomes.Values.OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => new ReportRow(o.Id, ledger.GetValueOrDefault(o.Id)?.State ?? "?", ledger.GetValueOrDefault(o.Id)?.Owner ?? "?",
                new Dictionary<string, string> { [o.Tfm] = o.Status.ToString().ToLowerInvariant() }, o.Status == Status.Pass ? null : o.Detail))
            .ToList();
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(Path.Combine(dir, $"spec-report.{SpecPaths.Tfm}.json"), JsonSerializer.Serialize(mine, options));

        var merged = new SortedDictionary<string, ReportRow>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(dir, "spec-report.net*.json"))
        foreach (var row in JsonSerializer.Deserialize<List<ReportRow>>(File.ReadAllText(file), options) ?? [])
        {
            if (merged.TryGetValue(row.Id, out var existing))
                foreach (var (tfm, status) in row.Outcome) existing.Outcome[tfm] = status;
            else merged[row.Id] = row with { Detail = null };
        }

        foreach (var line in ledger.Values.Where(l => l.FixtureToCome && !merged.ContainsKey(l.Id)))
            merged[line.Id] = new ReportRow(line.Id, line.State, line.Owner, new Dictionary<string, string>(), "fixture added by " + line.Owner);
        File.WriteAllText(Path.Combine(dir, "spec-report.json"), JsonSerializer.Serialize(merged.Values, options));
    }

    public sealed record ReportRow(string Id, string State, string Owner, Dictionary<string, string> Outcome, string? Detail);
}

public sealed class SpecTests
{
    static readonly Lazy<SpecHarness> Real = new(() => new SpecHarness(Build.Real));
    static readonly Lazy<SpecHarness> Target = new(() => new SpecHarness(Build.Target));

    static readonly Lazy<IReadOnlyDictionary<string, LedgerLine>> LedgerLines = new(() =>
        Ledger.Parse(File.ReadAllLines(SpecPaths.LedgerPath), []).ToDictionary(l => l.Id, StringComparer.Ordinal));

    public static TheoryData<string> FixtureIds()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(SpecPaths.FixturesDir).Order(StringComparer.Ordinal)) data.Add(Path.GetFileName(dir));
        return data;
    }

    /// <summary>The ledger rule: a pending fixture doesn't pass against the real build; a done one does.</summary>
    [Theory]
    [MemberData(nameof(FixtureIds))]
    public void Fixture_agrees_with_its_ledger_line(string id)
    {
        var fixture = Fixture.Load(Path.Combine(SpecPaths.FixturesDir, id));
        var outcome = Real.Value.Evaluate(fixture);
        SpecReport.Outcomes[id] = outcome;
        Assert.True(LedgerLines.Value.TryGetValue(id, out var line), $"{id} has no ledger line");
        var problem = Ledger.CheckOutcome(line!, outcome);
        Assert.True(problem is null, problem);
        TestContext.Current.TestOutputHelper?.WriteLine($"{id} [{outcome.Tfm}] {line!.State}: {outcome.Status}\n{outcome.Detail}");
    }

    /// <summary>Every fixture is consumer code against the target API: it compiles there, apart from what it expects.</summary>
    [Theory]
    [MemberData(nameof(FixtureIds))]
    public void Fixture_compiles_against_the_target_api(string id)
    {
        var outcome = Target.Value.Evaluate(Fixture.Load(Path.Combine(SpecPaths.FixturesDir, id)));
        Assert.True(outcome.Status != Status.Fail, outcome.Detail);
    }

    [Fact]
    public void Ledger_lines_and_fixtures_match_and_hashes_agree()
    {
        var problems = new List<string>();
        var ledger = Ledger.Parse(File.ReadAllLines(SpecPaths.LedgerPath), problems);
        problems.AddRange(Ledger.CheckStructure(ledger, Ledger.FixtureHashes(SpecPaths.FixturesDir)));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_ledger_owner_is_a_pr_in_the_stack()
    {
        string[] stack = ["04d", "04e", "13a", "05", "06", "07", "08a", "08b", "09", "10", "11", "12", "12b", "18", "19", "13b", "15", "16a", "16b", "17", "20a", "20b", "21", "22", "23a", "23b", "24", "25"];
        var unknown = LedgerLines.Value.Values.Where(l => !stack.Contains(l.Owner)).Select(l => $"{l.Id}: {l.Owner}").ToList();
        Assert.True(unknown.Count == 0, "unknown owners:\n" + string.Join("\n", unknown));
    }
}
