using System.Text.Json;
using Xunit;

namespace Resultful.EndToEnd;

// One test per ledger line: its end-to-end cases run, then the ledger rule decides. Today every line is pending
// and nothing passes against the 0.2 core, so each test passes and reports "pending".
public sealed class EndToEndTests
{
    private static readonly Lazy<Ledger> TheLedger = new(() => Ledger.Load());
    private static readonly List<object> Report = [];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static TheoryData<string> Lines()
    {
        var only = Environment.GetEnvironmentVariable("E2E_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new(Cases.All().Select(c => c.Ledger).Distinct().Where(id => only is null || only.Contains(id)).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public async Task Line_matches_its_ledger_state(string id)
    {
        var results = new List<CaseResult>();
        foreach (var c in Cases.All().Where(c => c.Ledger == id))
        {
            var r = await c.Run();
            results.Add(r with { Name = c.Name });
        }

        var ledger = TheLedger.Value;
        var state = ledger.State(id);
        var outcome = results.All(r => r.Passed) ? "pass" : "fail";
        TestContext.Current.SendDiagnosticMessage($"{id}: {state.ToString().ToLowerInvariant()}, {outcome} ({results.Count(r => r.Passed)}/{results.Count} cases)");
        foreach (var r in results) TestContext.Current.TestOutputHelper?.WriteLine($"{(r.Passed ? "pass" : "fail")}  {r.Name}\n      {r.Detail.Replace("\n", "\n      ", StringComparison.Ordinal)}");
        WriteReport(id, state, outcome, results);

        if (LedgerRule.Violation(id, ledger, results) is { } violation) Assert.Fail(violation);
    }

    // Every project under src/ packs. Its log is in the report as the PACK entry and in artifacts/e2e/<run>/pack.log.
    [Fact]
    public async Task Every_project_packs()
    {
        var feed = await Feed.Get();
        lock (Report)
        {
            Report.Add(new { id = "PACK", state = "-", outcome = feed.PackFailures.Count == 0 ? "pass" : "fail", cases = Array.Empty<object>(), log = feed.PackLog });
            File.WriteAllText(Path.Combine(Repo.RunDir, "e2e-report.json"), JsonSerializer.Serialize(Report, Indented));
        }

        Assert.True(feed.PackFailures.Count == 0, "dotnet pack failed for: " + string.Join(", ", feed.PackFailures) + "\n" + feed.PackLog);
    }

    private static void WriteReport(string id, LineState state, string outcome, List<CaseResult> results)
    {
        lock (Report)
        {
            Report.Add(new { id, state = state.ToString().ToLowerInvariant(), outcome, cases = results.Select(r => new { r.Name, r.Passed, r.Detail }) });
            File.WriteAllText(Path.Combine(Repo.RunDir, "e2e-report.json"), JsonSerializer.Serialize(Report, Indented));
        }
    }
}
