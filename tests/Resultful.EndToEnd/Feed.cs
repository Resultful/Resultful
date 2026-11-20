using System.IO.Compression;
using System.Xml.Linq;

namespace Resultful.EndToEnd;

// Every packable project under src/, packed once per run with a per-run version suffix, into a feed that the
// Shop restores Resultful* from and from nowhere else (GOAL.md, Known traps: "Stale packages").
internal sealed class Feed
{
    private static readonly Lazy<Task<Feed>> Instance = new(PackAsync);

    private Feed(string dir, string config, string packages, IReadOnlyDictionary<string, string> versions, string packLog)
    {
        Dir = dir;
        Config = config;
        Packages = packages;
        Versions = versions;
        PackLog = packLog;
    }

    public string Dir { get; }
    public string Config { get; }
    public string Packages { get; }
    public IReadOnlyDictionary<string, string> Versions { get; }
    public string PackLog { get; }

    public bool Has(string id) => Versions.ContainsKey(id);

    public static Task<Feed> Get() => Instance.Value;

    private static async Task<Feed> PackAsync()
    {
        var dir = Path.Combine(Repo.RunDir, "feed");
        Directory.CreateDirectory(dir);
        var log = new System.Text.StringBuilder();
        foreach (var project in Directory.GetFiles(Repo.Path("src"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            // dotnet pack skips a project with IsPackable=false, so "every packable project" is every project.
            var result = await Run.Dotnet(["pack", project, "-c", "Release", "-o", dir, $"-p:VersionSuffix=e2e.{Repo.RunId}", "-p:ContinuousIntegrationBuild=true"]);
            log.AppendLine($"pack {Path.GetRelativePath(Repo.Root, project)}: exit {result.ExitCode}");
            if (!result.Succeeded) log.AppendLine(result.Tail());
        }

        var versions = Directory.GetFiles(dir, "*.nupkg").Select(ReadIdentity).ToDictionary(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);
        var config = Path.Combine(Repo.RunDir, "nuget.config");
        File.WriteAllText(config, NuGetConfig(dir));
        return new Feed(dir, config, Path.Combine(Repo.RunDir, "packages"), versions, log.ToString());
    }

    private static (string Id, string Version) ReadIdentity(string nupkg)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/', StringComparison.Ordinal));
        using var stream = entry.Open();
        var metadata = XDocument.Load(stream).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        string Value(string name) => metadata.Elements().Single(e => e.Name.LocalName == name).Value;
        return (Value("id"), Value("version"));
    }

    // Package source mapping: Resultful and Resultful.* come from the run's feed only; everything else from nuget.org.
    public static string NuGetConfig(string feed) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="e2e" value="{feed}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
          <packageSourceMapping>
            <packageSource key="e2e">
              <package pattern="Resultful" />
              <package pattern="Resultful.*" />
            </packageSource>
            <packageSource key="nuget.org">
              <package pattern="*" />
            </packageSource>
          </packageSourceMapping>
        </configuration>
        """;

    // The MSBuild properties a Shop build from this feed needs.
    public IEnumerable<string> BuildProperties()
    {
        yield return "-p:ShopCore=package";
        yield return $"-p:RestoreConfigFile={Config}";
        yield return $"-p:RestorePackagesPath={Packages}";
        // The ledger never widens a forced build: each case compiles exactly the use cases it names.
        yield return "-p:ShopLedger=none";
        // The generator's output comes from the package or not at all: the stand-in proves nothing about the package.
        yield return "-p:ShopGeneratorStandIn=false";
        yield return $"-p:ShopAnalyzers={(Has("Resultful.Analyzers") ? "true" : "false")}";
        foreach (var (property, id) in new[] { ("ResultfulPackageVersion", "Resultful"), ("ResultfulAspNetCorePackageVersion", "Resultful.AspNetCore") })
        {
            // A library that isn't packed yet gets a version no feed has, so restore names it (NU1101/NU1102).
            yield return $"-p:{property}={(Versions.TryGetValue(id, out var v) ? v : "0.0.0-not-packed")}";
        }

        // The tooling is added only once it is packed; until then the code that needs it doesn't compile.
        if (Versions.TryGetValue("Resultful.Generators", out var generators)) yield return $"-p:ResultfulGeneratorsPackageVersion={generators}";
        if (Versions.TryGetValue("Resultful.Analyzers", out var analyzers)) yield return $"-p:ResultfulAnalyzersPackageVersion={analyzers}";

        // The bar for every packed build (VERIFY.md section 3).
        yield return "-p:TreatWarningsAsErrors=true";
        yield return "-p:AnalysisLevel=latest-all";
        yield return "-p:Nullable=enable";
    }
}
