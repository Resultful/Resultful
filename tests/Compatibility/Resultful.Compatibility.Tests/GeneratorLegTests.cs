using System.Diagnostics;
using System.Reflection;
using Xunit;

[assembly: AssemblyFixture(typeof(Resultful.Compatibility.Tests.PackedPackages))]

namespace Resultful.Compatibility.Tests;

// The compiler legs of the generator package, from the packed .nupkg (Known traps): each SDK loads the build for its
// Roslyn, and only the 5.x build on C# 15 emits [Union]. SDK 10.0.1xx is Roslyn 5.0, the version the 5.x build
// compiles against, so it is the leg that would report CS9057.
public sealed class GeneratorLegTests(PackedPackages packages)
{
    private static readonly string[] Common =
    [
        "match: order 1, locked 2, missing 3",
        "widen: True placing",
        "cases: OrderLocked|OrderNotFound",
        "zero: True",
    ];

    public static TheoryData<string, string, string, bool> Legs() => new()
    {
        // SDK feature band, consumer target, analyzer folder the SDK must load, [Union] emitted.
        { "9.0.300", "net8.0", "roslyn4.14", false },
        { "10.0.100", "net10.0", "roslyn5.0", false },
        { "10.0.400", "net10.0", "roslyn5.0", false },
        { "11.0.100", "net10.0", "roslyn5.0", false },
        { "11.0.100", "net11.0", "roslyn5.0", true },
    };

    [Theory]
    [MemberData(nameof(Legs))]
    public void A_consumer_builds_and_runs_with_the_generator_build_for_its_compiler(string sdk, string tfm, string folder, bool union)
    {
        using var leg = Leg.Create(packages, sdk);

        var build = leg.Dotnet("build", "Resultful.Compatibility.Shapes", "-c", "Release", $"-p:CompatTargetFramework={tfm}", $"-p:ResultfulVersion={packages.Version}", $"-p:RestorePackagesPath={leg.Packages}");
        Assert.True(build.ExitCode == 0, build.Output);
        Assert.DoesNotContain("CS9057", build.Output, StringComparison.Ordinal);
        Assert.Contains(" 0 Warning(s)", build.Output, StringComparison.Ordinal);

        // Each project was compiled with exactly one Resultful.Generators.dll, from this compiler's folder.
        foreach (var project in new[] { "Resultful.Compatibility.Domain", "Resultful.Compatibility.Shapes" })
        {
            var analyzers = File.ReadAllLines(Path.Combine(leg.Root, project, "obj", "Release", tfm, "analyzers.txt"))
                .Where(a => a.EndsWith("Resultful.Generators.dll", StringComparison.Ordinal))
                .Select(a => a.Replace('\\', '/'));
            Assert.EndsWith($"/analyzers/dotnet/{folder}/cs/Resultful.Generators.dll", Assert.Single(analyzers), StringComparison.Ordinal);
        }

        var run = leg.Dotnet(Path.Combine(leg.Root, "Resultful.Compatibility.Shapes", "bin", "Release", tfm, "Resultful.Compatibility.Shapes.dll"));
        Assert.True(run.ExitCode == 0, run.Output);
        string[] expected = union
            ? [.. Common, "union: True", "switch: 201,409,404", "error union: declined limit missing CardDeclined OrderNotFound|CardDeclined|CardExpired"]
            : [.. Common, "union: False"];
        Assert.Equal(expected, run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (union)
        {
            // The flat switch is exhaustive with no discard arm, so dropping one is a compile error.
            var dropped = leg.Dotnet("build", "Resultful.Compatibility.Shapes", "-c", "Release", $"-p:CompatTargetFramework={tfm}", $"-p:ResultfulVersion={packages.Version}", $"-p:RestorePackagesPath={leg.Packages}", "-p:CompatDropArm=true");
            Assert.NotEqual(0, dropped.ExitCode);
            Assert.Contains("CS8509", dropped.Output, StringComparison.Ordinal);
        }
    }
}

// Resultful and Resultful.Generators packed once per run, with a version no feed has (Known traps: stale packages).
public sealed class PackedPackages : IDisposable
{
    public PackedPackages()
    {
        RepoRoot = typeof(PackedPackages).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepoRootDir").Value!;
        var suffix = "ci." + (Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Output = Path.Combine(RepoRoot, "artifacts", "compat", suffix);
        Directory.CreateDirectory(Output);
        foreach (var project in new[] { "src/Resultful/Resultful.csproj", "src/Resultful.Generators/Resultful.Generators.csproj" })
        {
            var pack = Process.Run(RepoRoot, "pack", project, "-c", "Release", "-o", Output, $"-p:VersionSuffix={suffix}");
            Assert.True(pack.ExitCode == 0, pack.Output);
        }

        var generators = Assert.Single(Directory.GetFiles(Output, "Resultful.Generators.*.nupkg"));
        Version = Path.GetFileNameWithoutExtension(generators)["Resultful.Generators.".Length..];
        Assert.EndsWith(suffix, Version, StringComparison.Ordinal);
    }

    public string RepoRoot { get; }

    public string Output { get; }

    public string Version { get; }

    public void Dispose() => Directory.Delete(Output, recursive: true);
}

// A copy of ../Generators outside the repository, so none of its props or global.json apply, pinned to one SDK band
// and restoring Resultful* from the packed folder only, into its own packages folder.
internal sealed class Leg : IDisposable
{
    private Leg(string root) => Root = root;

    public string Root { get; }

    public string Packages => Path.Combine(Root, "packages");

    public static Leg Create(PackedPackages packages, string sdk)
    {
        var band = sdk[..^2];
        var installed = Process.Run(Path.GetTempPath(), "--list-sdks").Output;
        if (!installed.Split('\n').Any(l => l.StartsWith(band, StringComparison.Ordinal)))
        {
            // CI installs every leg's SDK; a developer machine may not have them all.
            Assert.False(Environment.GetEnvironmentVariable("CI") == "true", $"SDK {band}xx is not installed:\n{installed}");
            Assert.Skip($"SDK {band}xx is not installed");
        }

        var leg = new Leg(Path.Combine(Path.GetTempPath(), "resultful-compat-" + Guid.NewGuid().ToString("N")));
        Copy(Path.Combine(packages.RepoRoot, "tests", "Compatibility", "Generators"), leg.Root);
        File.WriteAllText(Path.Combine(leg.Root, "global.json"), $$"""{ "sdk": { "version": "{{sdk}}", "rollForward": "latestPatch" } }""");
        File.WriteAllText(Path.Combine(leg.Root, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="packed" value="{packages.Output}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="packed">
                  <package pattern="Resultful" />
                  <package pattern="Resultful.*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        File.WriteAllText(Path.Combine(leg.Root, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(leg.Root, "Directory.Packages.props"), "<Project />");
        return leg;
    }

    public (int ExitCode, string Output) Dotnet(params string[] arguments) => Process.Run(Root, arguments);

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            {
                continue;
            }

            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

internal static class Process
{
    public static (int ExitCode, string Output) Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The child picks its own SDK from global.json: drop what this process's host or MSBuild set.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase) || k is "MSBuildExtensionsPath" or "MSBuildSDKsPath").ToList())
        {
            start.Environment.Remove(key);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        using var process = System.Diagnostics.Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr.Result);
    }
}
