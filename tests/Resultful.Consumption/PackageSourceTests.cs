using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// Proves which Resultful build the suite ran against: the freshly packed package when
/// UsePackedPackage=true, otherwise the project reference.
/// </summary>
public sealed class PackageSourceTests
{
    [Fact]
    public void Referenced_Resultful_assembly_comes_from_the_expected_source()
    {
        var loaded = typeof(Result).Assembly.Location;
        var tfm = Path.GetFileName(Path.GetDirectoryName(loaded)!);
        Assert.StartsWith("net", tfm, StringComparison.Ordinal);

        var expected = AssemblyPaths.UsePackedPackage
            ? Path.Combine(AssemblyPaths.ResultfulPackageDir, "lib", NearestAsset(AssemblyPaths.ResultfulPackageDir, tfm), "Resultful.dll")
            : Path.Combine(AssemblyPaths.RepoRootDir, "src", "Resultful", "bin", AssemblyPaths.BuildConfiguration, tfm, "Resultful.dll");

        Assert.True(File.Exists(expected), $"Expected Resultful asset at {expected}");
        Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(loaded));

        if (AssemblyPaths.UsePackedPackage)
        {
            var packageDirName = new DirectoryInfo(AssemblyPaths.ResultfulPackageDir).Name;
            Assert.Equal(AssemblyPaths.ResultfulPackageVersion, packageDirName, ignoreCase: true);
        }
    }

    /// <summary>
    /// The lib folder NuGet picks for <paramref name="tfm"/>: its own, else the highest lower netX.Y, else
    /// netstandard2.0. A package without the target's own folder is restored that way, e.g. a net8.0 build takes
    /// netstandard2.0.
    /// </summary>
    private static string NearestAsset(string packageDir, string tfm)
    {
        var folders = Directory.GetDirectories(Path.Combine(packageDir, "lib")).Select(Path.GetFileName).OfType<string>().ToList();
        if (folders.Contains(tfm, StringComparer.OrdinalIgnoreCase))
        {
            return tfm;
        }

        static Version? NetVersion(string folder) =>
            folder.StartsWith("net", StringComparison.OrdinalIgnoreCase) && Version.TryParse(folder[3..], out var v) && v.Major >= 5 ? v : null;

        var wanted = NetVersion(tfm) ?? throw new InvalidOperationException($"not a .NET 5+ target: {tfm}");
        return folders.Where(f => NetVersion(f) is { } v && v <= wanted).OrderByDescending(NetVersion).FirstOrDefault()
            ?? folders.FirstOrDefault(f => f.Equals("netstandard2.0", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"no lib folder in {packageDir} suits {tfm}: {string.Join(", ", folders)}");
    }
}
