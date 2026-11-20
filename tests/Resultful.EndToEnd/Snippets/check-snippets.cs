// Doc-snippet check (stack-plan VERIFY.md section 5): each code block listed in snippets.tsv must equal the tagged
// regions of the Shop it names. Lenient by default (reports, exits 0); --strict (PR 25) exits 1 on any difference.
// --self-test shows --strict fails on a seeded difference. Moves to eng/check-snippets.cs at wiring.
//   dotnet run tests/Resultful.EndToEnd/Snippets/check-snippets.cs [-- --strict | --self-test]
using System.Text.RegularExpressions;

var strict = args.Contains("--strict");
if (args.Contains("--self-test")) return SelfTest();

var root = FindRoot(Directory.GetCurrentDirectory());
var map = Path.Combine(root, "tests", "Resultful.EndToEnd", "Snippets", "snippets.tsv");
var problems = Check(root, File.ReadAllLines(map), Console.Out);
Console.WriteLine(problems == 0 ? "snippets: every listed block equals the Shop" : $"snippets: {problems} difference(s){(strict ? "" : " (lenient: not failing; --strict fails)")}");
return strict && problems > 0 ? 1 : 0;

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
    var regions = Regions(root);
    var problems = 0;
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
        var at = Enumerable.Range(0, Math.Max(expected.Count, actual.Count)).FirstOrDefault(i => i >= expected.Count || i >= actual.Count || expected[i] != actual[i], -1);
        if (at < 0)
        {
            report.WriteLine($"ok      {doc} [{selector}]");
            continue;
        }

        problems++;
        report.WriteLine($"DIFFERS {doc} [{selector}] = {string.Join(" + ", names)}, from line {at + 1}:");
        report.WriteLine($"  doc:  {(at < expected.Count ? expected[at] : "(ends)")}");
        report.WriteLine($"  shop: {(at < actual.Count ? actual[at] : "(ends)")}");
    }

    return problems;
}

// `#region snippet: name` … `#endregion` in the Shop's sources and the broken consumers.
static Dictionary<string, List<string>> Regions(string root)
{
    var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    var dirs = new[] { Path.Combine(root, "samples"), Path.Combine(root, "tests", "Resultful.EndToEnd", "Broken") }.Where(Directory.Exists);
    foreach (var file in dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)).Where(f => !f.Contains("/obj/") && !f.Contains("/bin/")))
    {
        List<string>? current = null;
        foreach (var line in File.ReadLines(file))
        {
            var start = Regex.Match(line, @"^\s*#region snippet:\s*(\S+)");
            if (start.Success) { current = result[start.Groups[1].Value] = []; continue; }
            if (current is not null && Regex.IsMatch(line, @"^\s*#endregion")) { current = null; continue; }
            current?.Add(line);
        }
    }

    return result;
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

        File.WriteAllText(shop, "class A {\n    #region snippet: one\n        var x = 1;\n        return x;\n    #endregion\n    #region snippet: two\n    var y = 2;\n    #endregion\n}\n");
        var same = Check(dir.FullName, map, quiet);
        File.WriteAllText(shop, "class A {\n    #region snippet: one\n    var x = 1;\n    return x + 1;\n    #endregion\n    #region snippet: two\n    var y = 2;\n    #endregion\n}\n");
        var seeded = Check(dir.FullName, map, quiet);
        File.WriteAllText(shop, "class A { }\n");
        var gone = Check(dir.FullName, map, quiet);

        var ok = same == 0 && seeded == 1 && gone == 2;
        Console.WriteLine($"self-test: identical → {same} differences (want 0), seeded difference → {seeded} (want 1, so --strict exits 1), regions gone → {gone} (want 2): {(ok ? "pass" : "FAIL")}");
        return ok ? 0 : 1;
    }
    finally
    {
        dir.Delete(recursive: true);
    }
}
