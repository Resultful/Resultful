#:property PublishAot=false
// Packs every package as one 0.3.0 preview and proves the set is publishable, without pushing anything.
// Used by CI's preview-dry-run job (N = 0, on every pull request) and by the dispatched publish-preview job.
// Previews: docs/decisions/0007-ci-follows-rig.md.
//
// Usage: dotnet run eng/preview-packages.cs -- <N> <output-dir>
//
// 1. Packs every project under src/ with Version=0.3.0-preview.N (dotnet pack skips IsPackable=false), so every
//    package gets the same version and project references become dependencies on that version.
// 2. Fails unless every .nupkg has exactly that version, and so contains "-preview.".
// 3. Restores the whole set together, at that exact version, into a new empty packages folder: Resultful and
//    Resultful.* come from the output folder only, everything else from nuget.org.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length != 2 || !Regex.IsMatch(args[0], "^(0|[1-9][0-9]{0,8})$"))
{
    Console.Error.WriteLine("usage: dotnet run eng/preview-packages.cs -- <N> <output-dir>   (N: 0 for a dry run, else a positive integer)");
    return 2;
}

var version = $"0.3.0-preview.{args[0]}";
var output = Path.GetFullPath(args[1]);
var root = FindRoot();

if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
{
    Console.Error.WriteLine($"error: {output} is not empty; pack into a new folder so no stale package is checked or pushed.");
    return 1;
}

Directory.CreateDirectory(output);

// 1. Pack.
var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
foreach (var project in projects)
{
    Console.WriteLine($"pack {Path.GetRelativePath(root, project)} as {version}");
    if (Dotnet(root, "pack", project, "--configuration", "Release", "--output", output, $"-p:Version={version}", "-p:ContinuousIntegrationBuild=true") != 0)
    {
        Console.Error.WriteLine($"error: dotnet pack failed for {Path.GetRelativePath(root, project)}");
        return 1;
    }
}

// 2. Check the versions.
var packages = Directory.GetFiles(output, "*.nupkg").Order(StringComparer.Ordinal).Select(Identity).ToList();
if (packages.Count == 0)
{
    Console.Error.WriteLine("error: nothing was packed");
    return 1;
}

var wrong = packages.Where(p => p.Version != version || !p.Version.Contains("-preview.", StringComparison.Ordinal)).ToList();
foreach (var (id, packed) in packages)
{
    Console.WriteLine($"  {id} {packed}");
}

if (wrong.Count > 0)
{
    foreach (var (id, packed) in wrong)
    {
        Console.Error.WriteLine($"error: {id} was packed as {packed}, not {version}");
    }

    return 1;
}

// 3. Restore the set together into a clean packages folder. The probe lives outside the repository, so none of its
// Directory.Build.props, Directory.Packages.props or global.json apply.
var probe = Path.Combine(Path.GetTempPath(), $"resultful-preview-probe-{Guid.NewGuid():N}");
Directory.CreateDirectory(probe);
try
{
    var packagesFolder = Path.Combine(probe, "packages");
    File.WriteAllText(Path.Combine(probe, "nuget.config"), $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="preview" value="{output}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
          <packageSourceMapping>
            <packageSource key="preview">
              <package pattern="Resultful" />
              <package pattern="Resultful.*" />
            </packageSource>
            <packageSource key="nuget.org">
              <package pattern="*" />
            </packageSource>
          </packageSourceMapping>
        </configuration>
        """);
    var references = string.Join(Environment.NewLine, packages.Select(p => $"""    <PackageReference Include="{p.Id}" Version="[{version}]" />"""));
    File.WriteAllText(Path.Combine(probe, "Probe.csproj"), $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
            <RestorePackagesPath>{packagesFolder}</RestorePackagesPath>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          </PropertyGroup>
          <ItemGroup>
        {references}
          </ItemGroup>
        </Project>
        """);

    Console.WriteLine($"restore {packages.Count} package(s) together into {packagesFolder}");
    if (Dotnet(probe, "restore", Path.Combine(probe, "Probe.csproj"), "--configfile", Path.Combine(probe, "nuget.config")) != 0)
    {
        Console.Error.WriteLine("error: the packed set does not restore together");
        return 1;
    }

    var missing = packages.Where(p => !Directory.Exists(Path.Combine(packagesFolder, p.Id.ToLowerInvariant(), version))).ToList();
    foreach (var (id, _) in missing)
    {
        Console.Error.WriteLine($"error: {id} {version} was not restored from the packed set");
    }

    if (missing.Count > 0)
    {
        return 1;
    }

    Console.WriteLine($"ok: {packages.Count} package(s) packed as {version} and restored together; nothing was pushed");
    return 0;
}
finally
{
    Directory.Delete(probe, recursive: true);
}

static (string Id, string Version) Identity(string nupkg)
{
    using var zip = ZipFile.OpenRead(nupkg);
    var entry = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/', StringComparison.Ordinal));
    using var stream = entry.Open();
    var metadata = XDocument.Load(stream).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
    string Value(string name) => metadata.Elements().Single(e => e.Name.LocalName == name).Value;
    return (Value("id"), Value("version"));
}

static int Dotnet(string directory, params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start)!;
    process.WaitForExit();
    return process.ExitCode;
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Resultful.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("run from inside the repository (no Resultful.slnx found above the current folder)");
}
