// The target API gate (stack-plan VERIFY.md section 2, contract 04c).
//
//   dotnet run tests/Resultful.TargetApi/check-api.cs                          check every package against its target
//   dotnet run tests/Resultful.TargetApi/check-api.cs -- --strict              also: the real API equals the target (PR 25)
//   dotnet run tests/Resultful.TargetApi/check-api.cs -- --self-test           show that each rule can fail
//   dotnet run tests/Resultful.TargetApi/check-api.cs -- --regenerate-removed  rewrite removed-in-0.3.txt from the baseline
//
// For each package with a target here (tests/Resultful.TargetApi/<Package>/api/<tfm>/PublicAPI.Shipped.txt):
//   - the real API is read from src/<Package>/api/<tfm>/ (03's layout), or from one src/<Package>/PublicAPI.*.txt pair
//     for every target (the 0.2 layout). A target framework with no real file is a problem, never an empty API.
//     Real = Shipped minus its *REMOVED* lines, plus the Unshipped additions.
//   - the baseline is frozen here: baseline-0.2.0/<Package>/<tfm>/PublicAPI.Shipped.txt (0.2.0 as 04b ships it). A
//     package with no baseline is new in 0.3, so its baseline is empty. The editable Shipped file is never trusted.
//   1. real - baseline is in the target;
//   2. baseline - real is on removed-in-0.3.txt.
// It also reports, without failing, what is still to add and which listed removals are still to make.
// A package with no project under src/ yet is skipped and named; with --strict that is a problem. Exit code 1 lists every problem.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

var args2 = args.ToList();
if (args2.Contains("--self-test")) return SelfTest.Run();
var repo = args2.SkipWhile(a => a != "--repo").Skip(1).FirstOrDefault() ?? FindRepoRoot();
if (args2.Contains("--regenerate-removed")) return ApiGate.RegenerateRemoved(repo);
var report = ApiGate.Check(repo, args2.Contains("--strict"));
Console.Write(report.Text);
return report.Problems.Count == 0 ? 0 : 1;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run inside the repository (no global.json found).");
}

sealed record GateReport(List<string> Problems, string Text);

static class ApiGate
{
    const string Removed = "*REMOVED*";
    const string RemovedList = "removed-in-0.3.txt";

    static string TargetRoot(string repo) => Path.Combine(repo, "tests", "Resultful.TargetApi");

    static List<string> Packages(string repo) => Directory.GetDirectories(TargetRoot(repo))
        .Where(d => Directory.Exists(Path.Combine(d, "api")))
        .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();

    static List<string> Tfms(string repo, string package) => Directory.GetDirectories(Path.Combine(TargetRoot(repo), package, "api"))
        .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();

    static HashSet<string> Target(string repo, string package, string tfm) =>
        ReadLines(Path.Combine(TargetRoot(repo), package, "api", tfm, "PublicAPI.Shipped.txt")).ToHashSet(StringComparer.Ordinal);

    static HashSet<string> Baseline(string repo, string package, string tfm) =>
        ReadLines(Path.Combine(TargetRoot(repo), "baseline-0.2.0", package, tfm, "PublicAPI.Shipped.txt")).ToHashSet(StringComparer.Ordinal);

    /// <summary>The real API for one target, or null with the reason when its files are missing.</summary>
    static (HashSet<string>? Api, string? Missing) Real(string repo, string package, string tfm)
    {
        var realDir = Path.Combine(repo, "src", package);
        foreach (var dir in new[] { Path.Combine(realDir, "api", tfm), realDir })
        {
            var shippedPath = Path.Combine(dir, "PublicAPI.Shipped.txt");
            var unshippedPath = Path.Combine(dir, "PublicAPI.Unshipped.txt");
            if (!File.Exists(shippedPath) && !File.Exists(unshippedPath)) continue;
            if (!File.Exists(shippedPath) || !File.Exists(unshippedPath))
                return (null, $"{Path.GetRelativePath(repo, dir)} has only one of PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt");
            var unshipped = ReadLines(unshippedPath).ToList();
            var removed = unshipped.Where(l => l.StartsWith(Removed, StringComparison.Ordinal)).Select(l => l[Removed.Length..]).ToHashSet(StringComparer.Ordinal);
            return (ReadLines(shippedPath).Where(l => !removed.Contains(l))
                .Concat(unshipped.Where(l => !l.StartsWith(Removed, StringComparison.Ordinal))).ToHashSet(StringComparer.Ordinal), null);
        }

        return (null, $"no PublicAPI files for {tfm}: expected src/{package}/api/{tfm}/PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt");
    }

    public static GateReport Check(string repo, bool strict)
    {
        var problems = new List<string>();
        var text = new StringBuilder();
        var removedList = ReadLines(Path.Combine(TargetRoot(repo), RemovedList)).ToHashSet(StringComparer.Ordinal);
        var packages = Packages(repo);
        if (packages.Count == 0) problems.Add($"no target projects found under {TargetRoot(repo)}: the gate checked nothing");

        foreach (var package in packages)
        {
            if (!Directory.Exists(Path.Combine(repo, "src", package)))
            {
                text.AppendLine($"{package}: no project at src/{package} yet, skipped");
                if (strict) problems.Add($"{package}: no project at src/{package}, but every package must equal its target");
                continue;
            }

            foreach (var tfm in Tfms(repo, package))
            {
                var target = Target(repo, package, tfm);
                var baseline = Baseline(repo, package, tfm);
                var (real, missing) = Real(repo, package, tfm);
                if (real is null) { problems.Add($"{package} [{tfm}]: {missing}"); continue; }

                var added = real.Where(l => !baseline.Contains(l)).Order(StringComparer.Ordinal).ToList();
                var dropped = baseline.Where(l => !real.Contains(l)).Order(StringComparer.Ordinal).ToList();
                foreach (var line in added.Where(l => !target.Contains(l)))
                    problems.Add($"{package} [{tfm}]: added but not in the target API: {line}");
                foreach (var line in dropped.Where(l => !removedList.Contains(l)))
                    problems.Add($"{package} [{tfm}]: removed from 0.2.0 but not on {RemovedList}: {line}");

                var toAdd = target.Count(l => !real.Contains(l));
                var toRemove = real.Count(l => baseline.Contains(l) && removedList.Contains(l));
                text.AppendLine($"{package} [{tfm}]: {added.Count} added, {dropped.Count} removed since 0.2.0; {toAdd} target members still to add, {toRemove} listed removals still to make");

                if (strict)
                {
                    foreach (var line in target.Where(l => !real.Contains(l)).Order(StringComparer.Ordinal))
                        problems.Add($"{package} [{tfm}]: in the target but not in the real API: {line}");
                    foreach (var line in real.Where(l => !target.Contains(l)).Order(StringComparer.Ordinal))
                        problems.Add($"{package} [{tfm}]: in the real API but not in the target: {line}");
                }
            }
        }

        if (problems.Count > 0)
        {
            text.AppendLine($"API gate: {problems.Count} problem(s)");
            foreach (var p in problems) text.AppendLine("  " + p);
            text.AppendLine("A public change must already be in the target (tests/Resultful.TargetApi), and a removal on removed-in-0.3.txt.");
            text.AppendLine("A real change to the target is a separate `spec:` commit with a line in DECISIONS-TAKEN.md.");
        }
        else text.AppendLine("API gate: passed");
        return new GateReport(problems, text.ToString());
    }

    /// <summary>removed-in-0.3.txt is every baseline member missing from the target on some target framework.</summary>
    public static int RegenerateRemoved(string repo)
    {
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var package in Packages(repo))
        foreach (var tfm in Tfms(repo, package))
        {
            var target = Target(repo, package, tfm);
            lines.UnionWith(Baseline(repo, package, tfm).Where(l => !target.Contains(l)));
        }

        File.WriteAllLines(Path.Combine(TargetRoot(repo), RemovedList),
            new[]
            {
                "# 0.2.0 members (PublicAPI format) that the 0.3 target drops: baseline-0.2.0 minus the target, every target framework.",
                "# Regenerate with: dotnet run tests/Resultful.TargetApi/check-api.cs -- --regenerate-removed",
            }.Concat(lines));
        Console.WriteLine($"{RemovedList}: {lines.Count} members");
        return 0;
    }

    static IEnumerable<string> ReadLines(string path) => !File.Exists(path)
        ? []
        : File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
}

static class SelfTest
{
    const string Kept = "Resultful.Kept() -> void";
    const string Dropped = "Resultful.Dropped() -> void";
    const string New = "Resultful.New() -> void";

    // Builds a throwaway repository in 03's layout (src/Resultful/api/<tfm>/), then seeds each failure the gate must catch.
    public static int Run()
    {
        var failures = new List<string>();
        Expect("a clean change in 03's layout passes", repo => { }, 0);
        Expect("an addition outside the target fails", repo => Append(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Unshipped.txt", "Resultful.Seeded.Extra() -> void"), 1);
        Expect("an unlisted removal fails", repo => Append(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Unshipped.txt", "*REMOVED*" + Kept), 1);
        Expect("a member deleted from the editable Shipped file is caught by the baseline", repo =>
            Write(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Shipped.txt", "#nullable enable", Dropped), 1);
        Expect("a member slipped into the editable Shipped file is caught by the baseline", repo =>
            Append(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Shipped.txt", "Resultful.Sneaked() -> void"), 1);
        Expect("a target framework with no real files fails", repo =>
            Write(repo, "tests/Resultful.TargetApi/Resultful/api/net10.0/PublicAPI.Shipped.txt", "#nullable enable", Kept, New), 1);
        Expect("a target framework with half its files fails", repo =>
            File.Delete(Path.Combine(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Unshipped.txt")), 1);
        Expect("the 0.2 layout (one pair for every target) is read", repo =>
        {
            Directory.Delete(Path.Combine(repo, "src/Resultful/api"), recursive: true);
            Write(repo, "src/Resultful/PublicAPI.Shipped.txt", "#nullable enable", Kept, Dropped);
            Write(repo, "src/Resultful/PublicAPI.Unshipped.txt", "#nullable enable", New, "*REMOVED*" + Dropped);
        }, 0);
        Expect("a new package (no baseline) may only add target members", repo =>
        {
            Write(repo, "tests/Resultful.TargetApi/Resultful.Extra/api/net10.0/PublicAPI.Shipped.txt", "#nullable enable", "Resultful.Extra.A() -> void");
            Write(repo, "src/Resultful.Extra/api/net10.0/PublicAPI.Shipped.txt", "#nullable enable");
            Write(repo, "src/Resultful.Extra/api/net10.0/PublicAPI.Unshipped.txt", "#nullable enable", "Resultful.Extra.A() -> void", "Resultful.Extra.B() -> void");
        }, 1);
        Expect("--strict passes when the real API equals the target", repo => { }, 0, strict: true);
        Expect("--strict fails while a target member is missing", repo =>
            Write(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Unshipped.txt", "#nullable enable", "*REMOVED*" + Dropped), 1, strict: true);
        Expect("--strict fails on a package with no src project", repo =>
            Write(repo, "tests/Resultful.TargetApi/Resultful.Later/api/net10.0/PublicAPI.Shipped.txt", "#nullable enable", "Resultful.Later.A() -> void"), 1, strict: true);
        Expect("an empty target tree fails", repo => Directory.Delete(Path.Combine(repo, "tests/Resultful.TargetApi/Resultful"), recursive: true), 1);
        ExpectRegenerated(failures);

        foreach (var f in failures) Console.WriteLine("SELF-TEST FAILED: " + f);
        Console.WriteLine(failures.Count == 0 ? "check-api self-test: every rule fails when seeded" : $"check-api self-test: {failures.Count} failure(s)");
        return failures.Count == 0 ? 0 : 1;

        void Expect(string name, Action<string> seed, int expectProblems, bool strict = false)
        {
            var repo = Seeded();
            try
            {
                seed(repo);
                var report = ApiGate.Check(repo, strict);
                if (report.Problems.Count != expectProblems)
                    failures.Add($"{name}: expected {expectProblems} problem(s), got {report.Problems.Count}\n{report.Text}");
                else Console.WriteLine($"ok: {name}");
            }
            finally { Directory.Delete(repo, recursive: true); }
        }
    }

    static void ExpectRegenerated(List<string> failures)
    {
        var repo = Seeded();
        try
        {
            File.WriteAllText(Path.Combine(repo, "tests/Resultful.TargetApi/removed-in-0.3.txt"), "");
            ApiGate.RegenerateRemoved(repo);
            var lines = File.ReadAllLines(Path.Combine(repo, "tests/Resultful.TargetApi/removed-in-0.3.txt")).Where(l => !l.StartsWith('#')).ToList();
            if (lines.SequenceEqual([Dropped])) Console.WriteLine("ok: --regenerate-removed writes baseline minus target");
            else failures.Add("--regenerate-removed wrote: " + string.Join(", ", lines));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    static string Seeded()
    {
        var repo = Directory.CreateTempSubdirectory("check-api-").FullName;
        Write(repo, "tests/Resultful.TargetApi/removed-in-0.3.txt", Dropped);
        Write(repo, "tests/Resultful.TargetApi/baseline-0.2.0/Resultful/netstandard2.0/PublicAPI.Shipped.txt", "#nullable enable", Kept, Dropped);
        Write(repo, "tests/Resultful.TargetApi/Resultful/api/netstandard2.0/PublicAPI.Shipped.txt", "#nullable enable", Kept, New);
        Write(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Shipped.txt", "#nullable enable", Kept, Dropped);
        Write(repo, "src/Resultful/api/netstandard2.0/PublicAPI.Unshipped.txt", "#nullable enable", New, "*REMOVED*" + Dropped);
        return repo;
    }

    static void Write(string repo, string path, params string[] lines)
    {
        var full = Path.Combine(repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllLines(full, lines);
    }

    static void Append(string repo, string path, string line) => File.AppendAllLines(Path.Combine(repo, path), [line]);
}
