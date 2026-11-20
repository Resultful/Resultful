// The target API gate (stack-plan VERIFY.md section 2, contract 04c).
//
//   dotnet run tests/Resultful.TargetApi/check-api.cs                 check every package against its target
//   dotnet run tests/Resultful.TargetApi/check-api.cs -- --strict     also: the real API equals the target (PR 25)
//   dotnet run tests/Resultful.TargetApi/check-api.cs -- --self-test  show that each rule can fail
//
// For each package with a target project here (tests/Resultful.TargetApi/<Package>/PublicAPI/<tfm>/), and a real
// project at src/<Package>/ (per-target PublicAPI/<tfm>/ files, or one PublicAPI.*.txt pair for every target):
//   1. every addition in the real PublicAPI.Unshipped.txt is in the target's list for that target framework;
//   2. every shipped member missing from the real API (a *REMOVED* line) is on removed-in-0.3.txt.
// It also reports, without failing, how many listed removals are still in the real API.
// A package with no real project yet is skipped and named. Exit code 1 lists every problem.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

var args2 = args.ToList();
if (args2.Contains("--self-test")) return SelfTest.Run();
var strict = args2.Contains("--strict");
var repo = args2.SkipWhile(a => a != "--repo").Skip(1).FirstOrDefault() ?? FindRepoRoot();
var report = ApiGate.Check(repo, strict);
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

    public static GateReport Check(string repo, bool strict)
    {
        var problems = new List<string>();
        var text = new StringBuilder();
        var targetRoot = Path.Combine(repo, "tests", "Resultful.TargetApi");
        var removedList = ReadLines(Path.Combine(targetRoot, "removed-in-0.3.txt")).ToHashSet(StringComparer.Ordinal);
        var packages = Directory.GetDirectories(targetRoot)
            .Where(d => Directory.Exists(Path.Combine(d, "PublicAPI")))
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();
        if (packages.Count == 0) problems.Add($"no target projects found under {targetRoot}: the gate checked nothing");

        foreach (var package in packages)
        {
            var realDir = Path.Combine(repo, "src", package);
            if (!Directory.Exists(realDir)) { text.AppendLine($"{package}: no project at src/{package} yet, skipped"); continue; }
            foreach (var tfmDir in Directory.GetDirectories(Path.Combine(targetRoot, package, "PublicAPI")).Order(StringComparer.Ordinal))
            {
                var tfm = Path.GetFileName(tfmDir);
                var target = ReadLines(Path.Combine(tfmDir, "PublicAPI.Shipped.txt")).ToHashSet(StringComparer.Ordinal);
                var realTfmDir = Path.Combine(realDir, "PublicAPI", tfm);
                var apiDir = Directory.Exists(realTfmDir) ? realTfmDir : realDir;
                var shipped = ReadLines(Path.Combine(apiDir, "PublicAPI.Shipped.txt")).ToList();
                var unshipped = ReadLines(Path.Combine(apiDir, "PublicAPI.Unshipped.txt")).ToList();
                var removedNow = unshipped.Where(l => l.StartsWith(Removed, StringComparison.Ordinal)).Select(l => l[Removed.Length..]).ToHashSet(StringComparer.Ordinal);
                var added = unshipped.Where(l => !l.StartsWith(Removed, StringComparison.Ordinal)).ToList();
                var real = shipped.Where(l => !removedNow.Contains(l)).Concat(added).ToHashSet(StringComparer.Ordinal);

                foreach (var line in added.Where(l => !target.Contains(l)))
                    problems.Add($"{package} [{tfm}]: added but not in the target API: {line}");
                foreach (var line in removedNow.Where(l => !removedList.Contains(l)))
                    problems.Add($"{package} [{tfm}]: removed but not on removed-in-0.3.txt: {line}");

                var pending = real.Count(removedList.Contains);
                var missing = target.Count(l => !real.Contains(l));
                text.AppendLine($"{package} [{tfm}]: {added.Count} added, {removedNow.Count} removed; {missing} target members still to add, {pending} listed removals still to make");

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

    static IEnumerable<string> ReadLines(string path) => !File.Exists(path)
        ? []
        : File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
}

static class SelfTest
{
    // Builds a throwaway repository, then seeds each failure the gate must catch.
    public static int Run()
    {
        var failures = new List<string>();
        Expect("a clean change passes", repo => { }, expectProblems: 0);
        Expect("an addition outside the target fails", repo => Append(repo, "src/Resultful/PublicAPI.Unshipped.txt", "Resultful.Seeded.Extra() -> void"), expectProblems: 1);
        Expect("an unlisted removal fails", repo => Append(repo, "src/Resultful/PublicAPI.Unshipped.txt", "*REMOVED*Resultful.Kept() -> void"), expectProblems: 1);
        Expect("per-target real files are read per target", repo =>
        {
            Write(repo, "tests/Resultful.TargetApi/Resultful/PublicAPI/net10.0/PublicAPI.Shipped.txt", "#nullable enable", "Resultful.Kept() -> void", "Resultful.New() -> void");
            Write(repo, "src/Resultful/PublicAPI/net10.0/PublicAPI.Shipped.txt", "#nullable enable", "Resultful.Kept() -> void", "Resultful.Dropped() -> void");
            Write(repo, "src/Resultful/PublicAPI/net10.0/PublicAPI.Unshipped.txt", "#nullable enable", "Resultful.NetOnly() -> void");
        }, expectProblems: 1);
        Expect("--strict passes when the real API equals the target", repo => { }, expectProblems: 0, strict: true);
        Expect("--strict fails while a target member is missing", repo =>
            Write(repo, "src/Resultful/PublicAPI.Unshipped.txt", "#nullable enable", "*REMOVED*Resultful.Dropped() -> void"), expectProblems: 1, strict: true);
        Expect("an empty target tree fails", repo => Directory.Delete(Path.Combine(repo, "tests/Resultful.TargetApi/Resultful"), recursive: true), expectProblems: 1);

        foreach (var f in failures) Console.WriteLine("SELF-TEST FAILED: " + f);
        Console.WriteLine(failures.Count == 0 ? "check-api self-test: every rule fails when seeded" : $"check-api self-test: {failures.Count} failure(s)");
        return failures.Count == 0 ? 0 : 1;

        void Expect(string name, Action<string> seed, int expectProblems, bool strict = false)
        {
            var repo = Directory.CreateTempSubdirectory("check-api-").FullName;
            try
            {
                Write(repo, "tests/Resultful.TargetApi/removed-in-0.3.txt", "Resultful.Dropped() -> void");
                Write(repo, "tests/Resultful.TargetApi/Resultful/PublicAPI/netstandard2.0/PublicAPI.Shipped.txt", "#nullable enable", "Resultful.Kept() -> void", "Resultful.New() -> void");
                Write(repo, "src/Resultful/PublicAPI.Shipped.txt", "#nullable enable", "Resultful.Kept() -> void", "Resultful.Dropped() -> void");
                Write(repo, "src/Resultful/PublicAPI.Unshipped.txt", "#nullable enable", "Resultful.New() -> void", "*REMOVED*Resultful.Dropped() -> void");
                seed(repo);
                var report = ApiGate.Check(repo, strict);
                if (report.Problems.Count != expectProblems)
                    failures.Add($"{name}: expected {expectProblems} problem(s), got {report.Problems.Count}\n{report.Text}");
                else Console.WriteLine($"ok: {name}");
            }
            finally { Directory.Delete(repo, recursive: true); }
        }
    }

    static void Write(string repo, string path, params string[] lines)
    {
        var full = Path.Combine(repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllLines(full, lines);
    }

    static void Append(string repo, string path, string line) => File.AppendAllLines(Path.Combine(repo, path), [line]);
}
