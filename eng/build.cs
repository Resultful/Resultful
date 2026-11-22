#:sdk Cake.Sdk@7.0.0
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false
#:package NuGet.Versioning

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NuGet.Versioning;

var target = Argument("target", "CI");
var configuration = Argument("configuration", "Release");
var isCi = Argument("ci", false);
var runId = EnvironmentVariable("GITHUB_RUN_ID") ?? "local";
var runAttempt = EnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1";
var tempRoot = EnvironmentVariable("RUNNER_TEMP") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "resultful-build");

const string Solution = "Resultful.slnx";
const string CoreProject = "src/Resultful/Resultful.csproj";
const string UnitTests = "tests/Resultful.Tests/Resultful.Tests.csproj";
const string ConsumptionTests = "tests/Resultful.Consumption/Resultful.Consumption.csproj";
var ArtifactsDir = Argument("artifacts", "artifacts"); // the release workflow packs into artifacts/<package key>

void RunDotNet(string args)
{
    var exit = StartProcess("dotnet", new ProcessSettings { Arguments = args });
    if (exit != 0)
        throw new InvalidOperationException($"dotnet {args} failed with exit code {exit}");
}

string WritePackedNuGetConfig(string configPath, string sourceDir)
{
    var fullSource = System.IO.Path.GetFullPath(sourceDir);
    var xml = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="packed" value="{fullSource}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
          <packageSourceMapping>
            <packageSource key="packed">
              <package pattern="Resultful" />
            </packageSource>
            <packageSource key="nuget.org">
              <package pattern="*" />
            </packageSource>
          </packageSourceMapping>
        </configuration>
        """;
    EnsureDirectoryExists(System.IO.Path.GetDirectoryName(configPath)!);
    System.IO.File.WriteAllText(configPath, xml);
    return configPath;
}

string FindPackedVersion()
{
    var pkg = System.IO.Directory.GetFiles(ArtifactsDir, "Resultful.*.nupkg")
        .Select(System.IO.Path.GetFileNameWithoutExtension)
        .FirstOrDefault(n => n is not null && !n.EndsWith(".symbols", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No Resultful.*.nupkg found in {ArtifactsDir}");
    return pkg["Resultful.".Length..];
}

// The packed DLLs must stay AnyCPU, as every published Resultful is: IL only, PE32, machine I386, not 32-bit only.
// Package validation doesn't compare PE headers, so <PlatformTarget>x64</PlatformTarget> would pass it.
void VerifyAnyCpu(string nupkg)
{
    using var zip = System.IO.Compression.ZipFile.OpenRead(nupkg);
    var dlls = zip.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.FullName.EndsWith(".dll", StringComparison.Ordinal)).ToList();
    if (dlls.Count == 0)
        throw new InvalidOperationException($"{nupkg} has no lib/*.dll");
    foreach (var entry in dlls)
    {
        using var buffer = new System.IO.MemoryStream();
        using (var stream = entry.Open())
            stream.CopyTo(buffer);
        buffer.Position = 0;
        using var pe = new System.Reflection.PortableExecutable.PEReader(buffer);
        var headers = pe.PEHeaders;
        var flags = headers.CorHeader!.Flags;
        if (headers.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.I386
            || headers.PEHeader!.Magic != System.Reflection.PortableExecutable.PEMagic.PE32
            || !flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.ILOnly)
            || flags.HasFlag(System.Reflection.PortableExecutable.CorFlags.Requires32Bit))
            throw new InvalidOperationException($"{entry.FullName} in {nupkg} isn't AnyCPU: {headers.CoffHeader.Machine}, {headers.PEHeader!.Magic}, {flags}");
        Information($"AnyCPU: {entry.FullName}");
    }
}

Task("Restore")
    .Does(() => DotNetRestore(Solution));

Task("Format")
    .IsDependentOn("Restore")
    .Does(() => RunDotNet($"format {Solution} --no-restore --verify-no-changes"));

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        var ciProp = isCi ? " -p:ContinuousIntegrationBuild=true" : "";
        RunDotNet($"build {Solution} --configuration {configuration} --no-restore{ciProp}");
    });

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
    {
        RunDotNet($"test {UnitTests} --configuration {configuration} --no-build --logger \"trx\" --results-directory TestResults");
        foreach (var tfm in new[] { "net10.0", "net11.0" })
        {
            RunDotNet($"run --project {ConsumptionTests} --configuration {configuration} --no-build -f {tfm}");
            RunDotNet($"run --project {ConsumptionTests} --configuration {configuration} --no-build -f {tfm} -- -culture tr-TR");
        }
    });

Task("Pack")
    .IsDependentOn("Build")
    .Does(() =>
    {
        EnsureDirectoryExists(ArtifactsDir);
        var ciProp = isCi ? " -p:ContinuousIntegrationBuild=true" : "";
        RunDotNet($"pack {CoreProject} --configuration {configuration} --no-build{ciProp} -o {ArtifactsDir}");
        var nupkgs = string.Join(" ", System.IO.Directory.GetFiles(ArtifactsDir, "*.nupkg").Select(p => $"\"{p}\""));
        RunDotNet($"run eng/verify-package.cs -- {nupkgs}");
        foreach (var nupkg in System.IO.Directory.GetFiles(ArtifactsDir, "*.nupkg"))
            VerifyAnyCpu(nupkg);
    });

Task("TestPacked")
    .IsDependentOn("Pack")
    .Does(() =>
    {
        var suffix = $"ci.{runId}.{runAttempt}";
        var packedDir = System.IO.Path.Combine(tempRoot, "packed");
        var sourceDir = System.IO.Path.Combine(packedDir, "source");
        var packagesDir = System.IO.Path.Combine(packedDir, "packages");
        CleanDirectory(packedDir);
        EnsureDirectoryExists(sourceDir);

        var ciProp = isCi ? " -p:ContinuousIntegrationBuild=true" : "";
        RunDotNet($"pack {CoreProject} --configuration {configuration} --no-build{ciProp} -p:VersionSuffix={suffix} -o \"{sourceDir}\"");
        var config = WritePackedNuGetConfig(System.IO.Path.Combine(packedDir, "nuget.config"), sourceDir);
        var props = $"-p:UsePackedPackage=true -p:VersionSuffix={suffix}";
        RunDotNet($"restore {ConsumptionTests} --configfile \"{config}\" --packages \"{packagesDir}\" {props}");
        RunDotNet($"build {ConsumptionTests} --configuration {configuration} --no-restore {props}");
        foreach (var tfm in new[] { "net10.0", "net11.0" })
            RunDotNet($"run --project {ConsumptionTests} --configuration {configuration} --no-build -f {tfm} {props}");
    });

Task("CompatNuGet")
    .Does(() =>
    {
        var version = FindPackedVersion();
        var workDir = System.IO.Path.Combine(tempRoot, "compat-nuget");
        CleanDirectory(workDir);
        var config = WritePackedNuGetConfig(System.IO.Path.Combine(workDir, "nuget.config"), ArtifactsDir);
        var packagesDir = System.IO.Path.Combine(workDir, "packages");
        const string project = "tests/Compatibility/NuGet/Resultful.Compatibility.NuGet.csproj";
        var prop = $"-p:ResultfulPackageVersion={version}";
        RunDotNet($"restore {project} --configfile \"{config}\" --packages \"{packagesDir}\" {prop}");
        RunDotNet($"build {project} --configuration {configuration} --no-restore {prop}");
        foreach (var tfm in new[] { "net8.0", "net10.0", "net11.0" })
            RunDotNet($"run --project {project} --configuration {configuration} --no-build -f {tfm} {prop}");
    });

Task("CompatNet481")
    .Does(() =>
    {
        var version = FindPackedVersion();
        var workDir = System.IO.Path.Combine(tempRoot, "compat-net481");
        CleanDirectory(workDir);
        var config = WritePackedNuGetConfig(System.IO.Path.Combine(workDir, "nuget.config"), ArtifactsDir);
        var packagesDir = System.IO.Path.Combine(workDir, "packages");
        const string project = "tests/Compatibility/Net481/Resultful.Compatibility.Net481.csproj";
        var prop = $"-p:ResultfulPackageVersion={version}";
        RunDotNet($"restore {project} --configfile \"{config}\" --packages \"{packagesDir}\" {prop}");
        RunDotNet($"build {project} --configuration {configuration} --no-restore {prop}");
        RunDotNet($"run --project {project} --configuration {configuration} --no-build -f net481 {prop}");
    });

Task("CompatUpgrade")
    .Does(async () =>
    {
        // A library compiled against the last published pre-release, run by an app on the packed package.
        const string baseline = "0.2.0-beta05";
        var version = FindPackedVersion();
        var workDir = System.IO.Path.Combine(tempRoot, "compat-upgrade");
        var sourceDir = System.IO.Path.Combine(workDir, "source");
        CleanDirectory(workDir);
        EnsureDirectoryExists(sourceDir);
        System.IO.File.Copy(System.IO.Path.Combine(ArtifactsDir, $"Resultful.{version}.nupkg"), System.IO.Path.Combine(sourceDir, $"Resultful.{version}.nupkg"));
        using (var http = new System.Net.Http.HttpClient())
        {
            var bytes = await http.GetByteArrayAsync($"https://api.nuget.org/v3-flatcontainer/resultful/{baseline}/resultful.{baseline}.nupkg");
            await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(sourceDir, $"Resultful.{baseline}.nupkg"), bytes);
        }

        var config = WritePackedNuGetConfig(System.IO.Path.Combine(workDir, "nuget.config"), sourceDir);
        var packagesDir = System.IO.Path.Combine(workDir, "packages");
        const string project = "tests/Compatibility/Upgrade/App/Resultful.Compatibility.Upgrade.csproj";
        var prop = $"-p:ResultfulPackageVersion={version}";
        RunDotNet($"restore {project} --configfile \"{config}\" --packages \"{packagesDir}\" {prop}");
        RunDotNet($"build {project} --configuration {configuration} --no-restore {prop}");
        foreach (var tfm in new[] { "net8.0", "net10.0" })
            RunDotNet($"run --project {project} --configuration {configuration} --no-build -f {tfm} {prop}");
    });

Task("CI")
    .IsDependentOn("Format")
    .IsDependentOn("Test")
    .IsDependentOn("TestPacked");

// Release targets, run by .github/workflows/release.yml in its read-only jobs (decision 22):
//   ReleasePlan            validate PACKAGES (e.g. "Resultful=0.2.0", or JSON {"Resultful":"0.2.0"}) against
//                          .github/release-packages.json, nuget.org and the tags; write plan_json to $GITHUB_OUTPUT.
//                          Draft releases are invisible to this read-only job: release.yml's `draft` job checks those
//   ReleaseProps           write the gitignored release-versions.props from $PLAN_JSON
//   ReleaseCheckArtifacts  require artifacts/<key>/ to hold exactly the planned packages
const string ManifestPath = ".github/release-packages.json";
const string PropsPath = "release-versions.props";
const string ReleaseArtifactsRoot = "artifacts";

async System.Threading.Tasks.Task AnnotateFailure(Func<System.Threading.Tasks.Task> action)
{
    try { await action(); }
    catch (ReleaseException e)
    {
        Console.Error.WriteLine($"::error::{e.Message}");
        throw;
    }
}

Task("ReleasePlan").Does(() => AnnotateFailure(ReleasePlan));
Task("ReleaseProps").Does(() => AnnotateFailure(() => { ReleaseProps(); return System.Threading.Tasks.Task.CompletedTask; }));
Task("ReleaseCheckArtifacts").Does(() => AnnotateFailure(() => { ReleaseCheckArtifacts(); return System.Threading.Tasks.Task.CompletedTask; }));

async System.Threading.Tasks.Task ReleasePlan()
{
    var dryRun = !string.Equals(Env("DRY_RUN", "true"), "false", StringComparison.OrdinalIgnoreCase);
    var gitRef = Env("GITHUB_REF", "");
    var sha = Env("GITHUB_SHA", "") is { Length: > 0 } s ? s : Git("rev-parse", "HEAD");
    if (!dryRun && gitRef != "refs/heads/main")
        throw new ReleaseException($"a real release runs only on main, not {gitRef}; use dry_run to try another ref.");

    var requested = ParseMap(Env("PACKAGES", ""));
    var manifest = ReadReleaseManifest();

    var tags = Git("tag", "--list").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    using var http = new HttpClient();
    var plan = new List<ReleasePlanEntry>();
    var failures = new List<string>();

    foreach (var (key, versionText) in requested)
    {
        if (!manifest.TryGetValue(key, out var package))
        {
            failures.Add($"{key} isn't in {ManifestPath} (known: {string.Join(", ", manifest.Keys)}).");
            continue;
        }

        if (!NuGetVersion.TryParseStrict(versionText, out var version) || version.HasMetadata)
        {
            failures.Add($"{key}: '{versionText}' isn't a SemVer 2.0 version (no 'v' prefix, no build metadata), e.g. 0.2.0 or 0.2.0-beta07.");
            continue;
        }

        var normalized = version.ToNormalizedString();
        var tag = $"{key}/v{normalized}";
        var published = await PublishedVersions(http, package.PackageId);
        var isPublished = published.Contains(version);

        // A tag is made only when finalize.yml publishes the version's draft release, so an existing tag means done.
        if (tags.Contains(tag))
            failures.Add($"{key}: {tag} already exists, at {Git("rev-list", "-n", "1", tag)}; {normalized} is released.");
        else if (isPublished)
            failures.Add($"{key}: {package.PackageId} {normalized} is on nuget.org but not tagged; run finalize.yml for its draft release {tag}.");
        if (key == "Resultful" && tags.Contains($"v{normalized}")) // older releases tagged only the core, as v<version>
            failures.Add($"{key}: the older tag v{normalized} already exists; {normalized} was released before this workflow.");

        // Monotonic within a major.minor line, so a fix to an older line (0.1.x after 0.2.0) is still allowed.
        var line = published.Where(v => v.Major == version.Major && v.Minor == version.Minor && v != version).ToList();
        if (!isPublished && line.Count > 0 && line.Max()! >= version)
            failures.Add($"{key}: {normalized} isn't above {line.Max()!.ToNormalizedString()}, already published on the {version.Major}.{version.Minor} line.");

        foreach (var coupled in package.CoupledWith.Where(c => !requested.Any(r => r.Key == c)))
            failures.Add($"{key} is coupled with {coupled}; release both together.");
        foreach (var dependency in package.DependsOn.Where(d => !requested.Any(r => r.Key == d)))
        {
            if (manifest.TryGetValue(dependency, out var dep) && (await PublishedVersions(http, dep.PackageId)).Count == 0)
                failures.Add($"{key} depends on {dependency}, which has never been published; release it in the same run.");
        }

        // Package validation against the newest stable on nuget.org that this version must stay compatible with:
        // the same major (>= 1.0), or the same major.minor (0.x, where a minor may break).
        var baseline = published
            .Where(v => !v.IsPrerelease && v < version && v.Major == version.Major && (version.Major > 0 || v.Minor == version.Minor))
            .DefaultIfEmpty()
            .Max();

        plan.Add(new ReleasePlanEntry(
            key, package.PackageId, normalized, tag, version.IsPrerelease, baseline?.ToNormalizedString() ?? "",
            Notes(key, package, version, tags)));
    }

    if (failures.Count > 0)
        throw new ReleaseException("the release map was rejected:%0A" + string.Join("%0A", failures.Select(f => "- " + f)));

    var json = JsonSerializer.Serialize(plan, ReleasePlanJson.Options);
    AppendTo("GITHUB_OUTPUT", $"plan_json={json}\n");
    var summary = new StringBuilder($"### Release plan{(dryRun ? " (dry run)" : "")} for `{sha}`\n\n| Package | Version | Tag | Baseline |\n|---|---|---|---|\n");
    foreach (var p in plan)
        summary.Append(CultureInfo.InvariantCulture, $"| {p.PackageId} | {p.Version} | `{p.Tag}` | {(p.Baseline is "" ? "none" : p.Baseline)} |\n");
    AppendTo("GITHUB_STEP_SUMMARY", summary.ToString());
    Console.WriteLine(json);
}

void ReleaseProps()
{
    var plan = ReadReleasePlan();
    var xml = new StringBuilder("<Project>\n  <!-- Written by eng/build.cs (ReleaseProps) for one release run (decision 22); never committed. -->\n");
    foreach (var p in plan)
    {
        var version = SemanticVersion.Parse(p.Version);
        xml.Append(CultureInfo.InvariantCulture, $"  <PropertyGroup Condition=\"'$(MSBuildProjectName)' == '{p.Key}'\">\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <VersionPrefix>{version.Major}.{version.Minor}.{version.Patch}</VersionPrefix>\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <VersionSuffix>{version.Release}</VersionSuffix>\n");
        if (p.Baseline is not "")
            xml.Append(CultureInfo.InvariantCulture, $"    <PackageValidationBaselineVersion>{p.Baseline}</PackageValidationBaselineVersion>\n");
        xml.Append("  </PropertyGroup>\n");
    }
    xml.Append("</Project>\n");
    System.IO.File.WriteAllText(PropsPath, xml.ToString());
    Console.WriteLine(xml);
}

void ReleaseCheckArtifacts()
{
    // The whole tree, not only the planned folders: publish pushes every .nupkg it is handed.
    var expected = ReadReleasePlan()
        .SelectMany(p => new[] { $"{p.Key}/{p.PackageId}.{p.Version}.nupkg", $"{p.Key}/{p.PackageId}.{p.Version}.snupkg" })
        .Order(StringComparer.Ordinal).ToArray();
    var found = System.IO.Directory.Exists(ReleaseArtifactsRoot)
        ? System.IO.Directory.GetFiles(ReleaseArtifactsRoot, "*", System.IO.SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(ReleaseArtifactsRoot, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray()
        : [];
    if (!found.SequenceEqual(expected))
        throw new ReleaseException($"{ReleaseArtifactsRoot}/ holds [{string.Join(", ", found)}], expected exactly [{string.Join(", ", expected)}].");

    // finalize.yml publishes a draft only if the package on nuget.org records the draft's commit (decision 23).
    var sha = Env("GITHUB_SHA", "") is { Length: > 0 } s ? s : Git("rev-parse", "HEAD");
    foreach (var nupkg in found.Where(f => f.EndsWith(".nupkg", StringComparison.Ordinal)))
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(System.IO.Path.Combine(ReleaseArtifactsRoot, nupkg));
        var entry = zip.Entries.Single(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = entry.Open();
        var commit = System.Xml.Linq.XDocument.Load(stream).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "repository")?.Attribute("commit")?.Value;
        if (commit != sha)
            throw new ReleaseException($"{nupkg} records repository commit '{commit}', not {sha}; finalize.yml could not match it to its draft.");
    }
    Console.WriteLine($"artifacts/ holds exactly the planned packages at their planned versions, built from {sha}.");
}

static List<KeyValuePair<string, string>> ParseMap(string text)
{
    text = text.Trim();
    var entries = new List<KeyValuePair<string, string>>();
    if (text.StartsWith('{'))
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException e) { throw new ReleaseException($"packages isn't valid JSON: {e.Message}"); }
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new ReleaseException($"packages: {property.Name}'s version must be a string.");
            entries.Add(new(property.Name, property.Value.GetString()!));
        }
    }
    else
    {
        foreach (var item in text.Split([' ', ',', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=');
            if (parts.Length != 2 || parts[0] == "" || parts[1] == "")
                throw new ReleaseException($"packages: '{item}' isn't Package=version.");
            entries.Add(new(parts[0], parts[1]));
        }
    }

    if (entries.Count == 0)
        throw new ReleaseException("packages is empty; give e.g. Resultful=0.2.0.");
    var duplicates = entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    if (duplicates.Count > 0)
        throw new ReleaseException($"packages names {string.Join(", ", duplicates)} more than once.");
    return entries;
}

static Dictionary<string, ReleasePackage> ReadReleaseManifest()
{
    var root = JsonNode.Parse(System.IO.File.ReadAllText(ManifestPath))!;
    if (root["schema_version"]?.GetValue<int>() != 1)
        throw new ReleaseException($"{ManifestPath}: schema_version must be 1.");
    return root["packages"]!.AsObject().ToDictionary(
        p => p.Key,
        p => new ReleasePackage(
            p.Value!["package_id"]!.GetValue<string>(),
            p.Value["inputs"]?.AsArray().Select(i => i!.GetValue<string>()).ToArray() ?? [p.Value["path"]!.GetValue<string>()],
            p.Value["depends_on"]?.AsArray().Select(i => i!.GetValue<string>()).ToArray() ?? [],
            p.Value["coupled_with"]?.AsArray().Select(i => i!.GetValue<string>()).ToArray() ?? []),
        StringComparer.Ordinal);
}

static async Task<HashSet<NuGetVersion>> PublishedVersions(HttpClient http, string packageId)
{
    var response = await http.GetAsync($"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json");
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        return [];
    response.EnsureSuccessStatusCode();
    var index = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    return index["versions"]!.AsArray().Select(v => NuGetVersion.Parse(v!.GetValue<string>())).ToHashSet();
}

// Commits since the package's previous release that touched its inputs. Older v<version> tags count as Resultful's.
string Notes(string key, ReleasePackage package, NuGetVersion version, HashSet<string> tags)
{
    var previous = tags
        .Select(t => (Tag: t, Text: t.StartsWith($"{key}/v", StringComparison.Ordinal) ? t[(key.Length + 2)..] : key == "Resultful" && t.StartsWith('v') ? t[1..] : null))
        .Where(t => t.Text is not null && SemanticVersion.TryParse(t.Text, out var v) && v < version)
        .OrderByDescending(t => SemanticVersion.Parse(t.Text!))
        .Select(t => t.Tag)
        .FirstOrDefault();
    var range = previous is null ? "HEAD" : $"{previous}..HEAD";
    var log = Git(["log", "--no-merges", "--max-count=50", "--format=- %s (%h)", range, "--", .. package.Inputs]);
    var since = previous is null ? "the first release from the release workflow" : previous;
    var body = log.Length == 0 ? "No changes to this package's files." : log;
    return $"Changes to {package.PackageId} since {since}:\n\n{body}\n\nSee RELEASE_NOTES.md for the full notes.";
}

static List<ReleasePlanEntry> ReadReleasePlan()
{
    var json = Environment.GetEnvironmentVariable("PLAN_JSON") ?? throw new ReleaseException("PLAN_JSON isn't set.");
    return JsonSerializer.Deserialize<List<ReleasePlanEntry>>(json, ReleasePlanJson.Options) ?? throw new ReleaseException("PLAN_JSON is empty.");
}

static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) ?? fallback;

static void AppendTo(string variable, string text)
{
    if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } path)
        System.IO.File.AppendAllText(path, text);
}

static string Git(params string[] arguments) => RunCaptured("git", arguments).Output;

static (int Exit, string Output) RunCaptured(string file, string[] arguments)
{
    var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments)
        start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new ReleaseException($"{file} {string.Join(' ', arguments)} failed ({process.ExitCode}): {error.Trim()}");
    return (process.ExitCode, output.Trim());
}

RunTarget(target);

sealed record ReleasePackage(string PackageId, string[] Inputs, string[] DependsOn, string[] CoupledWith);

sealed record ReleasePlanEntry(
    string Key, string PackageId, string Version, string Tag, bool Prerelease, string Baseline,
    string Notes);

static class ReleasePlanJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

sealed class ReleaseException(string message) : Exception(message);
