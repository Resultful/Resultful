#:sdk Cake.Sdk@7.0.0
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false

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
const string ArtifactsDir = "artifacts";

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

RunTarget(target);
