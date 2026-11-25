#:property PublishAot=false
// Pack-and-inspect check for Resultful packages (docs/decisions/0005-package-shape-is-checked.md).
//
// Usage: dotnet run eng/verify-package.cs -- <path-to-nupkg> [<path-to-nupkg> ...]
//
// Fails if a package regresses to the 0.2.0-beta05 shape that broke Paket/NuGet audit consumers:
//   - a NETStandard.Library or System.ValueTuple dependency
//   - a .NETStandard1.x or .NETFramework dependency group (beta05 had a bogus ".NETFramework5.0")
//   - a lib/netstandard1.x asset, no lib/net10.0 asset, or, in Resultful and Resultful.OneOf (the hardcoded
//     netStandardPackages list), no lib/netstandard2.0 asset; other packages, such as Resultful.Text.Json, target
//     net10.0 and net11.0 only (System.Text.Json is in the box there)
//   - any dependency at all in the core, Resultful (decision 1); it may have no dependency groups
//   - in Resultful.OneOf, a missing OneOf dependency or a floor below 3.0.271 (3.0.178 pulls NETStandard.Library 1.6.1
//     -> NU1903 on net8+)
//   - in any other package, a dependency group without Resultful at this package's own version (lockstep)
// and checks the metadata (licence, readme, repository commit) and the sibling .snupkg.
//
// Analyzer packages (analyzerPackages: Resultful.Generators) are checked instead for: no lib/ and no dependencies,
// developmentDependency, exactly one DLL in each of analyzers/dotnet/roslyn4.14/cs/ and roslyn5.0/cs/ (the SDK parses
// the folder as roslyn<major>.<minor>, so "roslyn5.x" would load in every compiler next to the 4.14 build), and each
// DLL's Microsoft.CodeAnalysis reference no newer than its folder's version (or that compiler reports CS9057).

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run eng/verify-package.cs -- <nupkg> [<nupkg> ...]");
    return 2;
}

var minOneOf = new Version(3, 0, 271);
string[] banned = ["NETStandard.Library", "System.ValueTuple"];
string[] netStandardPackages = ["Resultful", "Resultful.OneOf"];
string[] analyzerPackages = ["Resultful.Generators"];
string[] roslynFolders = ["roslyn4.14", "roslyn5.0"];
var exit = 0;
var versions = new Dictionary<string, string>(StringComparer.Ordinal);

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

    var version = metadata.Element(ns + "version")!.Value;
    versions[id] = version;
    var isAnalyzer = analyzerPackages.Contains(id);
    if (isAnalyzer)
    {
        failures.AddRange(dependencies.Select(d => $"the analyzer package depends on {d.Attribute("id")!.Value}"));
        failures.AddRange(files.Where(f => f.StartsWith("lib/", StringComparison.Ordinal)).Select(f => $"the analyzer package ships {f}"));
        if (metadata.Element(ns + "developmentDependency")?.Value != "true")
        {
            failures.Add("the analyzer package is not a developmentDependency");
        }

        var dlls = files.Where(f => f.EndsWith(".dll", StringComparison.Ordinal)).ToList();
        var expected = roslynFolders.Select(r => $"analyzers/dotnet/{r}/cs/{id}.dll").ToList();
        failures.AddRange(expected.Except(dlls).Select(f => $"missing {f}"));
        failures.AddRange(dlls.Except(expected).Select(f => $"unexpected {f}"));
        foreach (var entry in zip.Entries.Where(e => expected.Contains(e.FullName)))
        {
            var folder = Version.Parse(entry.FullName.Split('/')[2]["roslyn".Length..]);
            var reference = CodeAnalysisReference(entry);
            if (reference is null)
            {
                failures.Add($"{entry.FullName} references no Microsoft.CodeAnalysis");
            }
            else if (new Version(reference.Major, reference.Minor) > folder)
            {
                failures.Add($"{entry.FullName} references Microsoft.CodeAnalysis {reference}, newer than its folder");
            }
            else
            {
                Console.WriteLine($"  {entry.FullName}: Microsoft.CodeAnalysis {reference}");
            }
        }
    }
    else if (id == "Resultful")
    {
        // The core references nothing (decision 1): no dependency in any group. Empty groups, or none, are fine.
        failures.AddRange(dependencies.Select(d => $"the core depends on {d.Attribute("id")!.Value}"));
    }
    else if (groups.Count == 0)
    {
        failures.Add("nuspec has no dependency groups");
    }

    foreach (var group in id == "Resultful" || isAnalyzer ? [] : groups)
    {
        var tfm = (string?)group.Attribute("targetFramework") ?? "";
        var ids = group.Elements(ns + "dependency").ToDictionary(d => (string)d.Attribute("id")!, d => (string?)d.Attribute("version") ?? "");
        if (id == "Resultful.OneOf" && !ids.ContainsKey("OneOf"))
        {
            failures.Add($"{tfm} group has no OneOf dependency");
        }

        if (!ids.TryGetValue("Resultful", out var core) || core.Trim('[', ']', '(', ')').Split(',')[0] != version)
        {
            failures.Add($"{tfm} group does not depend on Resultful {version}");
        }
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

    if (netStandardPackages.Contains(id) && !files.Contains($"lib/netstandard2.0/{id}.dll"))
    {
        failures.Add($"missing lib/netstandard2.0/{id}.dll");
    }

    if (!isAnalyzer && !files.Contains($"lib/net10.0/{id}.dll"))
    {
        failures.Add($"missing lib/net10.0/{id}.dll");
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
    if (!isAnalyzer && !File.Exists(snupkg))
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

// An analyzer package has no Resultful dependency to carry the lockstep version, so compare it with the core packed
// alongside it.
if (versions.TryGetValue("Resultful", out var coreVersion))
{
    foreach (var analyzer in analyzerPackages.Where(versions.ContainsKey).Where(a => versions[a] != coreVersion))
    {
        Console.Error.WriteLine($"Package check FAILED: {analyzer} {versions[analyzer]} is not the version of Resultful {coreVersion} packed with it (lockstep)");
        exit = 1;
    }
}

return exit;

static Version? CodeAnalysisReference(ZipArchiveEntry entry)
{
    using var buffer = new MemoryStream();
    using (var stream = entry.Open())
    {
        stream.CopyTo(buffer);
    }

    buffer.Position = 0;
    using var pe = new PEReader(buffer);
    var reader = pe.GetMetadataReader();
    return reader.AssemblyReferences
        .Select(reader.GetAssemblyReference)
        .Where(r => reader.GetString(r.Name) == "Microsoft.CodeAnalysis")
        .Select(r => r.Version)
        .FirstOrDefault();
}
