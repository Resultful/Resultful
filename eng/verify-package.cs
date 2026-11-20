#:property PublishAot=false
// Pack-and-inspect check for Resultful packages (docs/decisions/0005-package-shape-is-checked.md).
//
// Usage: dotnet run eng/verify-package.cs -- <path-to-nupkg> [<path-to-nupkg> ...]
//
// Fails if a package regresses to the 0.2.0-beta05 shape that broke Paket/NuGet audit consumers:
//   - a NETStandard.Library or System.ValueTuple dependency
//   - a .NETStandard1.x or .NETFramework dependency group (beta05 had a bogus ".NETFramework5.0")
//   - a lib/netstandard1.x asset, or no lib/netstandard2.0 asset
//   - a OneOf floor below 3.0.271 (3.0.178 pulls NETStandard.Library 1.6.1 -> NU1903 on net8+)
// and checks the metadata (licence, readme, repository commit) and the sibling .snupkg.

using System.IO.Compression;
using System.Xml.Linq;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run eng/verify-package.cs -- <nupkg> [<nupkg> ...]");
    return 2;
}

var minOneOf = new Version(3, 0, 271);
string[] banned = ["NETStandard.Library", "System.ValueTuple"];
var exit = 0;

foreach (var path in args)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"error: {path} not found");
        return 2;
    }

    var failures = new List<string>();
    using var zip = ZipFile.OpenRead(path);
    var files = zip.Entries.Select(e => e.FullName).ToList();
    var nuspecEntry = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal) && !e.FullName.Contains('/'));
    XDocument nuspec;
    using (var stream = nuspecEntry.Open())
    {
        nuspec = XDocument.Load(stream);
    }

    var ns = nuspec.Root!.Name.Namespace;
    var metadata = nuspec.Root.Element(ns + "metadata")!;
    var id = metadata.Element(ns + "id")!.Value;
    var groups = metadata.Element(ns + "dependencies")?.Elements(ns + "group").ToList() ?? [];
    var dependencies = groups.SelectMany(g => g.Elements(ns + "dependency")).ToList();

    if (groups.Count == 0)
    {
        failures.Add("nuspec has no dependency groups");
    }

    foreach (var group in groups)
    {
        var tfm = (string?)group.Attribute("targetFramework") ?? "";
        if (tfm.StartsWith(".NETStandard1.", StringComparison.Ordinal) || tfm.StartsWith(".NETFramework", StringComparison.Ordinal))
        {
            failures.Add($"nuspec has a {tfm} dependency group");
        }
    }

    foreach (var dependency in dependencies)
    {
        var depId = (string)dependency.Attribute("id")!;
        var range = ((string?)dependency.Attribute("version") ?? "").Trim('[', ']', '(', ')').Split(',')[0];
        if (banned.Contains(depId, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"nuspec depends on {depId}");
        }

        if (depId.Equals("OneOf", StringComparison.OrdinalIgnoreCase) && (!Version.TryParse(range, out var floor) || floor < minOneOf))
        {
            failures.Add($"OneOf floor {range} is below {minOneOf}");
        }
    }

    if (files.Any(f => f.StartsWith("lib/netstandard1.", StringComparison.Ordinal)))
    {
        failures.Add("package ships a lib/netstandard1.x assembly");
    }

    if (!files.Contains($"lib/netstandard2.0/{id}.dll"))
    {
        failures.Add($"missing lib/netstandard2.0/{id}.dll");
    }

    var license = metadata.Element(ns + "license");
    if ((string?)license?.Attribute("type") != "expression" || license?.Value != "MIT")
    {
        failures.Add("missing MIT license expression");
    }

    if (metadata.Element(ns + "readme")?.Value != "README.md" || !files.Contains("README.md"))
    {
        failures.Add("README.md missing from metadata or package");
    }

    var repository = metadata.Element(ns + "repository");
    if ((string?)repository?.Attribute("url") != "https://github.com/Resultful/Resultful.git")
    {
        failures.Add("missing repository url");
    }

    var commit = (string?)repository?.Attribute("commit") ?? "";
    if (commit.Length != 40 || !commit.All(Uri.IsHexDigit))
    {
        failures.Add("repository metadata has no commit (SourceLink)");
    }

    var snupkg = Path.ChangeExtension(path, ".snupkg");
    if (!File.Exists(snupkg))
    {
        failures.Add($"missing symbols package {Path.GetFileName(snupkg)}");
    }

    if (failures.Count > 0)
    {
        Console.Error.WriteLine($"Package check FAILED for {Path.GetFileName(path)}:");
        foreach (var f in failures.Distinct()) { Console.Error.WriteLine($"  - {f}"); }
        exit = 1;
        continue;
    }

    Console.WriteLine($"Package check passed for {Path.GetFileName(path)}");
    Console.WriteLine($"  dependency groups: {string.Join(' ', groups.Select(g => (string?)g.Attribute("targetFramework")))}");
    Console.WriteLine($"  dependencies: {string.Join(' ', dependencies.Select(d => $"{d.Attribute("id")!.Value} {d.Attribute("version")!.Value}").Distinct())}");
}

return exit;
