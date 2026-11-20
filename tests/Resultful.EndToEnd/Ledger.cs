namespace Resultful.EndToEnd;

internal enum LineState { Pending, Done, Missing }

// tests/Resultful.Spec/ledger.tsv: id, state, owner, sha256. The end-to-end cases share its lines with the spec
// harness: a line is done only when its fixture and every end-to-end case on it pass.
internal sealed class Ledger
{
    private readonly Dictionary<string, string> _states;

    private Ledger(bool exists, Dictionary<string, string> states)
    {
        Exists = exists;
        _states = states;
    }

    public bool Exists { get; }

    public static Ledger Load(string? path = null)
    {
        path ??= Repo.Path("tests", "Resultful.Spec", "ledger.tsv");
        if (!File.Exists(path)) return new Ledger(false, []);
        var states = File.ReadAllLines(path)
            .Skip(1)
            .Select(l => l.TrimEnd('\r').Split('\t'))
            .Where(f => f.Length >= 2 && f[0].Length > 0)
            .ToDictionary(f => f[0], f => f[1], StringComparer.Ordinal);
        return new Ledger(true, states);
    }

    public static Ledger From(params (string Id, string State)[] lines) =>
        new(true, lines.ToDictionary(l => l.Id, l => l.State, StringComparer.Ordinal));

    public LineState State(string id) => _states.TryGetValue(id, out var s)
        ? s == "done" ? LineState.Done : LineState.Pending
        : LineState.Missing;
}

internal sealed record CaseResult(string Name, bool Passed, string Detail);

// The same rule as the spec harness: a pending line whose cases all pass fails ("flip this line"); a done line
// whose cases don't all pass fails; a case with no line fails once the ledger exists.
internal static class LedgerRule
{
    public static string? Violation(string id, Ledger ledger, IReadOnlyList<CaseResult> results)
    {
        var passed = results.Count > 0 && results.All(r => r.Passed);
        return ledger.State(id) switch
        {
            LineState.Missing when ledger.Exists => $"{id}: no line in ledger.tsv; add one (state pending) for these end-to-end cases",
            LineState.Missing or LineState.Pending when passed => $"{id}: pending, but every end-to-end case passes: flip this line to done",
            LineState.Done when !passed => $"{id}: done, but end-to-end cases fail:\n" + string.Join("\n", results.Where(r => !r.Passed).Select(r => $"  {r.Name}: {r.Detail}")),
            _ => null,
        };
    }
}
