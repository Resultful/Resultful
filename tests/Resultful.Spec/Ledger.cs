using System.Security.Cryptography;
using System.Text;

namespace Resultful.Spec;

/// <summary>One line of ledger.tsv: a capability ID, its state, the PR that flips it, and its fixture's hash.</summary>
public sealed record LedgerLine(string Id, string State, string Owner, string Sha256, int LineNumber)
{
    public const string Pending = "pending";
    public const string Done = "done";

    /// <summary>A line whose fixture a later PR adds (the Shop's use cases and HTTP cases): its hash is `-`.</summary>
    public bool FixtureToCome => Sha256 == "-";
}

public static class Ledger
{
    public const string Header = "id\tstate\towner\tsha256";

    public static IReadOnlyList<LedgerLine> Parse(IEnumerable<string> lines, List<string> problems)
    {
        var result = new List<LedgerLine>();
        var n = 0;
        foreach (var raw in lines)
        {
            n++;
            var line = raw.TrimEnd('\r');
            if (n == 1)
            {
                if (line != Header) problems.Add($"ledger.tsv:1: the header must be `{Header.Replace('\t', ' ')}` (tab-separated)");
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cells = line.Split('\t');
            if (cells.Length != 4) { problems.Add($"ledger.tsv:{n}: expected 4 tab-separated cells, got {cells.Length}"); continue; }
            if (cells[1] is not (LedgerLine.Pending or LedgerLine.Done)) problems.Add($"ledger.tsv:{n}: state must be `pending` or `done`, got `{cells[1]}`");
            result.Add(new LedgerLine(cells[0], cells[1], cells[2], cells[3], n));
        }

        foreach (var dup in result.GroupBy(l => l.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"ledger.tsv: {dup.Key} appears {dup.Count()} times");
        return result;
    }

    /// <summary>SHA-256 over every file in the fixture directory: sorted relative paths and their LF-normalized text.</summary>
    public static string Hash(string fixtureDirectory)
    {
        var builder = new StringBuilder();
        foreach (var path in Directory.GetFiles(fixtureDirectory, "*", SearchOption.AllDirectories)
                     .Select(p => Path.GetRelativePath(fixtureDirectory, p).Replace('\\', '/'))
                     .Order(StringComparer.Ordinal))
        {
            builder.Append(path).Append('\n');
            builder.Append(File.ReadAllText(Path.Combine(fixtureDirectory, path)).Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>The structural rules: every fixture has a line, every line a fixture (or `-`), and the hashes match.</summary>
    public static List<string> CheckStructure(IReadOnlyList<LedgerLine> ledger, IReadOnlyDictionary<string, string> fixtureHashes)
    {
        var problems = new List<string>();
        var ids = ledger.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in fixtureHashes.Keys.Where(id => !ids.Contains(id)).Order(StringComparer.Ordinal))
            problems.Add($"Fixtures/{id} has no line in ledger.tsv (add `{id}\tpending\t<owner>\t{fixtureHashes[id]}`)");
        foreach (var line in ledger)
        {
            if (line.FixtureToCome)
            {
                if (fixtureHashes.TryGetValue(line.Id, out var h)) problems.Add($"ledger.tsv:{line.LineNumber}: {line.Id} now has a fixture: replace `-` with its hash {h}");
                if (line.State == LedgerLine.Done) problems.Add($"ledger.tsv:{line.LineNumber}: {line.Id} is done but has no fixture");
                continue;
            }

            if (!fixtureHashes.TryGetValue(line.Id, out var hash)) problems.Add($"ledger.tsv:{line.LineNumber}: {line.Id} has no directory under Fixtures/");
            else if (!string.Equals(hash, line.Sha256, StringComparison.Ordinal))
                problems.Add($"ledger.tsv:{line.LineNumber}: {line.Id}'s fixture changed (hash {hash}, ledger says {line.Sha256}). A change to a fixture is a `spec:` commit with a line in DECISIONS-TAKEN.md.");
        }

        return problems;
    }

    /// <summary>The state rules: a pending fixture must not pass; a done fixture must pass wherever it applies.</summary>
    public static string? CheckOutcome(LedgerLine line, Outcome outcome) => (line.State, outcome.Status) switch
    {
        (LedgerLine.Pending, Status.Pass) => $"{line.Id} passes on {outcome.Tfm} but is `pending`: flip this line to `done` (owner {line.Owner}).",
        (LedgerLine.Done, Status.Fail) => $"{line.Id} is `done` but fails on {outcome.Tfm}:\n{outcome.Detail}",
        _ => null,
    };

    public static IReadOnlyDictionary<string, string> FixtureHashes(string fixturesDir) => Directory.GetDirectories(fixturesDir)
        .ToDictionary(d => Path.GetFileName(d), Hash, StringComparer.Ordinal);
}
