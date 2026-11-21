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
            ? Path.Combine(AssemblyPaths.ResultfulPackageDir, "lib", tfm, "Resultful.dll")
            : Path.Combine(AssemblyPaths.RepoRootDir, "src", "Resultful", "bin", AssemblyPaths.BuildConfiguration, tfm, "Resultful.dll");

        Assert.True(File.Exists(expected), $"Expected Resultful asset at {expected}");
        Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(loaded));

        if (AssemblyPaths.UsePackedPackage)
        {
            var packageDirName = new DirectoryInfo(AssemblyPaths.ResultfulPackageDir).Name;
            Assert.Equal(AssemblyPaths.ResultfulPackageVersion, packageDirName, ignoreCase: true);
        }
    }
}
