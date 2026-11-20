using System.Text.Json.Nodes;
using Xunit;

namespace Resultful.EndToEnd;

// Each rule the runner enforces can fail: seeded inputs, no builds.
public sealed class RunnerSelfTests
{
    // The HTTP map (DECISIONS-TAKEN, 04c).
    private static readonly int[] MappedStatuses = [200, 400, 404, 409, 500];
    private static readonly CaseResult Pass = new("a", true, "");
    private static readonly CaseResult Fail = new("b", false, "broke");

    [Fact]
    public void A_pending_line_whose_cases_pass_must_be_flipped() =>
        Assert.Contains("flip this line", LedgerRule.Violation("UC01", Ledger.From(("UC01", "pending")), [Pass]), StringComparison.Ordinal);

    [Fact]
    public void A_pending_line_with_a_failing_case_is_fine() =>
        Assert.Null(LedgerRule.Violation("UC01", Ledger.From(("UC01", "pending")), [Pass, Fail]));

    [Fact]
    public void A_done_line_with_a_failing_case_fails() =>
        Assert.Contains("broke", LedgerRule.Violation("UC01", Ledger.From(("UC01", "done")), [Pass, Fail]), StringComparison.Ordinal);

    [Fact]
    public void A_done_line_whose_cases_pass_is_fine() =>
        Assert.Null(LedgerRule.Violation("UC01", Ledger.From(("UC01", "done")), [Pass]));

    [Fact]
    public void A_case_with_no_ledger_line_fails() =>
        Assert.Contains("no line", LedgerRule.Violation("UC11", Ledger.From(("UC01", "done")), [Fail]), StringComparison.Ordinal);

    [Fact]
    public void Sarif_ids_are_compared_both_ways()
    {
        Assert.Null(Sarif.Compare(Set("RF0001"), Set("RF0001")));
        Assert.Contains("missing RF0001", Sarif.Compare(Set("RF0001"), Set()), StringComparison.Ordinal);
        Assert.Contains("unexpected CS0246", Sarif.Compare(Set("RF0001"), Set("RF0001", "CS0246")), StringComparison.Ordinal);
    }

    [Fact]
    public void Sarif_reads_only_error_level_results()
    {
        var path = Path.Combine(Repo.RunDir, "self-test.sarif");
        File.WriteAllText(path, """
            { "version": "2.1.0", "runs": [ { "results": [
              { "ruleId": "CS8509", "level": "error" },
              { "ruleId": "CS1591", "level": "warning" },
              { "ruleId": "RF0005", "level": "note" } ] } ] }
            """);
        Assert.Equal(Set("CS8509"), Sarif.ErrorIds(path));
    }

    [Fact]
    public void A_build_that_succeeds_never_matches_a_broken_consumer() =>
        Assert.False(Cases.CompareSarif("x", new ProcessResult(0, ""), "none.sarif", "none.txt").Passed);

    [Fact]
    public void Json_bodies_match_as_a_subset()
    {
        var actual = JsonNode.Parse("""{ "status": 400, "title": "t", "errors": { "name": ["name is required"] }, "traceId": "x" }""");
        Assert.Null(JsonSubset.Mismatch(JsonNode.Parse("""{ "status": 400, "errors": { "name": ["name is required"] } }"""), actual));
        Assert.NotNull(JsonSubset.Mismatch(JsonNode.Parse("""{ "status": 404 }"""), actual));
        Assert.NotNull(JsonSubset.Mismatch(JsonNode.Parse("""{ "errors": { "name": ["other"] } }"""), actual));
        Assert.NotNull(JsonSubset.Mismatch(JsonNode.Parse("""{ "detail": "x" }"""), actual));
        Assert.True(JsonSubset.Has(actual, "errors.name"));
        Assert.False(JsonSubset.Has(actual, "errors.email"));
    }

    [Fact]
    public void Every_http_case_names_a_leaf_or_a_success_and_a_known_status()
    {
        var cases = HttpCases.Load();
        Assert.NotEmpty(cases);
        Assert.All(cases, c => Assert.Contains(c.Status, MappedStatuses));
        Assert.All(cases.Where(c => c.Status >= 400), c => Assert.True(c.Problem, $"{c.Key} must be a ProblemDetails"));
        Assert.All(cases.Where(c => c.Status == 500), c => Assert.Contains("detail", c.Absent));
        Assert.Equal(cases.Count, cases.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void Every_broken_consumer_has_a_project_and_expected_ids()
    {
        var dirs = Directory.GetDirectories(Repo.Path("tests", "Resultful.EndToEnd", "Broken")).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.Equal(Cases.Broken.Select(b => b.Name).Order(StringComparer.Ordinal), dirs);
        Assert.All(Cases.Broken, b => Assert.NotEmpty(Sarif.ReadExpected(Repo.Path("tests", "Resultful.EndToEnd", "Broken", b.Name, "expected.txt"))));
    }

    [Fact]
    public void The_feed_maps_resultful_to_itself_only()
    {
        var config = Feed.NuGetConfig("/feed");
        Assert.Contains("""<package pattern="Resultful.*" />""", config, StringComparison.Ordinal);
        Assert.Contains("""<add key="e2e" value="/feed" />""", config, StringComparison.Ordinal);
    }

    // The ledger fixtures for the Shop's lines (tests/Resultful.Spec/Fixtures/UC*/, HTTP-CASES/) pin the oracle files
    // they are judged by, so editing one without a `spec:` change to the fixture fails here, whatever the line's state.
    [Fact]
    public void The_oracle_files_match_the_pins_in_their_ledger_fixtures()
    {
        var pins = Directory.GetFiles(Repo.Path("tests", "Resultful.Spec", "Fixtures"), "EndToEnd.cs", SearchOption.AllDirectories)
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\\(\"(?<path>[^\"]+)\", \"(?<sha>[0-9a-f]{64})\"\\)")
                .Select(m => (Fixture: Path.GetFileName(Path.GetDirectoryName(f)!), Path: m.Groups["path"].Value, Sha: m.Groups["sha"].Value)))
            .ToList();
        Assert.Contains(pins, p => p.Path == "tests/Resultful.EndToEnd/http-cases.json");
        foreach (var pin in pins)
        {
            var text = File.ReadAllText(Repo.Path(pin.Path)).Replace("\r\n", "\n", StringComparison.Ordinal);
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            Assert.True(sha == pin.Sha, $"{pin.Path} changed (sha256 {sha}); Fixtures/{pin.Fixture} pins {pin.Sha}. Changing an oracle file is a `spec:` commit: update the pin and the ledger hash.");
        }
    }

    private static HashSet<string> Set(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);
}
