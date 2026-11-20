// Doc-snippet check (stack-plan VERIFY.md section 5): each code block listed in snippets.tsv must equal the tagged
// regions of the Shop it names. Lenient by default (reports, exits 0); --strict (PR 25) exits 1 on any difference.
// --self-test shows --strict fails on a seeded difference. Moves to eng/check-snippets.cs at wiring.
//   dotnet run tests/Resultful.EndToEnd/Snippets/check-snippets.cs [-- --strict | --self-test]
using System.Text.RegularExpressions;

if (args.Contains("--self-test")) return SelfTest();

var root = FindRoot(Directory.GetCurrentDirectory());
var map = Path.Combine(root, "tests", "Resultful.EndToEnd", "Snippets", "snippets.tsv");
return Run(root, File.ReadAllLines(map), args.Contains("--strict"), Console.Out);

// The exit code: 0 when lenient or when nothing differs; 1 under --strict when anything differs.
static int Run(string root, string[] mapLines, bool strict, TextWriter report)
{
    var problems = Check(root, mapLines, report);
    report.WriteLine(problems == 0 ? "snippets: every listed block equals the Shop" : $"snippets: {problems} problem(s){(strict ? "" : " (lenient: not failing; --strict fails)")}");
    return strict && problems > 0 ? 1 : 0;
}

static string FindRoot(string dir)
{
    for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
    {
        if (File.Exists(Path.Combine(d.FullName, "Resultful.slnx"))) return d.FullName;
    }

    throw new InvalidOperationException("run from inside the repository");
}

// Returns the number of blocks that differ or can't be found, writing a report for each.
static int Check(string root, string[] mapLines, TextWriter report)
{
    var (regions, duplicates) = Regions(root);
    var problems = duplicates.Count;
    foreach (var d in duplicates) report.WriteLine($"DUPLICATE region {d}: a region name must be unique");
    foreach (var line in mapLines.Where(l => l.Length > 0 && !l.StartsWith('#')).Skip(1))
    {
        var f = line.Split('\t');
        var (doc, selector, names) = (f[0], f[1], f[2].Split('+'));
        var block = Block(Path.Combine(root, doc), selector);
        var missing = names.Where(n => !regions.ContainsKey(n)).ToList();
        if (block is null || missing.Count > 0)
        {
            problems++;
            report.WriteLine($"MISSING {doc} [{selector}]: {(block is null ? "no such block" : "no region " + string.Join(", ", missing))}");
            continue;
        }

        var expected = Normalize(block);
        var actual = names.SelectMany(n => Normalize(regions[n])).ToList();
        var diff = Diff(expected, actual);
        if (diff.Count == 0)
        {
            report.WriteLine($"ok      {doc} [{selector}]");
            continue;
        }

        problems++;
        report.WriteLine($"DIFFERS {doc} [{selector}] = {string.Join(" + ", names)} (- doc, + shop):");
        foreach (var d in diff) report.WriteLine("  " + d);
    }

    return problems;
}

// Every differing line, as a line diff over the longest common subsequence.
static List<string> Diff(List<string> a, List<string> b)
{
    var lcs = new int[a.Count + 1, b.Count + 1];
    for (var i = a.Count - 1; i >= 0; i--)
        for (var j = b.Count - 1; j >= 0; j--)
            lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
    var result = new List<string>();
    int x = 0, y = 0;
    while (x < a.Count || y < b.Count)
    {
        if (x < a.Count && y < b.Count && a[x] == b[y]) { x++; y++; }
        else if (y < b.Count && (x == a.Count || lcs[x, y + 1] >= lcs[x + 1, y])) result.Add("+ " + b[y++]);
        else result.Add("- " + a[x++]);
    }

    return result;
}

// `#region snippet: name` … `#endregion` in the Shop's sources and the broken consumers.
static (Dictionary<string, List<string>> Regions, List<string> Duplicates) Regions(string root)
{
    var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    var duplicates = new List<string>();
    var dirs = new[] { Path.Combine(root, "samples"), Path.Combine(root, "tests", "Resultful.EndToEnd", "Broken") }.Where(Directory.Exists);
    foreach (var file in dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)).Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "obj" or "bin")))
    {
        List<string>? current = null;
        foreach (var line in File.ReadLines(file))
        {
            var start = Regex.Match(line, @"^\s*#region snippet:\s*(\S+)");
            if (start.Success)
            {
                var name = start.Groups[1].Value;
                if (result.ContainsKey(name)) duplicates.Add($"{name} (again in {Path.GetRelativePath(root, file)})");
                current = result[name] = [];
                continue;
            }
            if (current is not null && Regex.IsMatch(line, @"^\s*#endregion")) { current = null; continue; }
            current?.Add(line);
        }
    }

    return (result, duplicates);
}

// The n-th fenced block under a heading (`## 1. An endpoint#1`), or the block after `<!-- snippet: name -->`.
static List<string>? Block(string doc, string selector)
{
    if (!File.Exists(doc)) return null;
    var lines = File.ReadAllLines(doc);
    var start = 0;
    var n = 1;
    if (selector.StartsWith("tag:", StringComparison.Ordinal))
    {
        start = Array.FindIndex(lines, l => l.Trim() == $"<!-- snippet: {selector[4..]} -->");
        if (start < 0) return null;
    }
    else
    {
        var hash = selector.LastIndexOf('#');
        var heading = selector[..hash];
        n = int.Parse(selector[(hash + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        start = Array.FindIndex(lines, l => l.StartsWith(heading, StringComparison.Ordinal));
        if (start < 0) return null;
    }

    var level = selector.StartsWith("tag:", StringComparison.Ordinal) ? 0 : selector.TakeWhile(c => c == '#').Count();
    for (var i = start + 1; i < lines.Length; i++)
    {
        if (level > 0 && lines[i].StartsWith('#') && lines[i].TakeWhile(c => c == '#').Count() <= level && !lines[i].StartsWith("#!", StringComparison.Ordinal)) return null;
        if (!lines[i].StartsWith("```", StringComparison.Ordinal)) continue;
        var end = Array.FindIndex(lines, i + 1, l => l.StartsWith("```", StringComparison.Ordinal));
        if (end < 0) return null;
        if (--n == 0) return lines[(i + 1)..end].ToList();
        i = end;
    }

    return null;
}

// Blank lines and indentation don't count; every other character does.
static List<string> Normalize(IEnumerable<string> lines) => lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

static int SelfTest()
{
    var dir = Directory.CreateTempSubdirectory("check-snippets-");
    try
    {
        Directory.CreateDirectory(Path.Combine(dir.FullName, "samples", "Shop"));
        Directory.CreateDirectory(Path.Combine(dir.FullName, "docs"));
        File.WriteAllText(Path.Combine(dir.FullName, "docs", "guide.md"), "# Guide\n\n## 1. Orders\n\n```csharp\nvar x = 1;\n\nreturn x;\n```\n\n<!-- snippet: two -->\n```csharp\nvar y = 2;\n```\n");
        var shop = Path.Combine(dir.FullName, "samples", "Shop", "Shop.cs");
        string[] map = ["doc\tblock\tregions", "docs/guide.md\t## 1. Orders#1\tone", "docs/guide.md\ttag:two\ttwo"];
        var quiet = TextWriter.Null;

        const string same = "class A {\n    #region snippet: one\n        var x = 1;\n        return x;\n    #endregion\n    #region snippet: two\n    var y = 2;\n    #endregion\n}\n";
        var checks = new List<(string What, int Got, int Want)>();
        void Expect(string what, string source, bool strict, int want)
        {
            File.WriteAllText(shop, source);
            checks.Add((what, Run(dir.FullName, map, strict, quiet), want));
        }

        Expect("identical, --strict", same, true, 0);
        Expect("seeded difference, --strict", same.Replace("return x;", "return x + 1;", StringComparison.Ordinal), true, 1);
        Expect("seeded difference, lenient", same.Replace("return x;", "return x + 1;", StringComparison.Ordinal), false, 0);
        Expect("region gone, --strict", "class A { }\n", true, 1);
        Expect("duplicate region, --strict", same.Replace("}\n", "    #region snippet: two\n    var y = 2;\n    #endregion\n}\n", StringComparison.Ordinal), true, 1);

        // Every differing line is reported, not just the first.
        var diff = Diff(["a", "b", "c", "d"], ["a", "x", "c", "y"]);
        checks.Add(("every differing line", diff.Count, 4));

        foreach (var (what, got, want) in checks) Console.WriteLine($"self-test: {what} → {got} (want {want}){(got == want ? "" : "  FAIL")}");
        var ok = checks.All(c => c.Got == c.Want);
        Console.WriteLine($"self-test: {(ok ? "pass" : "FAIL")}");
        return ok ? 0 : 1;
    }
    finally
    {
        dir.Delete(recursive: true);
    }
}
