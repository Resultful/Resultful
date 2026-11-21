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

Task("CI")
    .IsDependentOn("Format")
    .IsDependentOn("Test")
    .IsDependentOn("TestPacked");

RunTarget(target);
