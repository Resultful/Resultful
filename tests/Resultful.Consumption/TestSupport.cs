using System.Reflection;
using Resultful;

namespace Resultful.Consumption;

internal readonly record struct TestError(string Message) : IPlus<TestError, TestError>
{
    public TestError Plus(TestError item) => new($"{Message}; {item.Message}");
}

internal readonly record struct AddableInt(int Value) : IPlus<AddableInt, AddableInt>
{
    public AddableInt Plus(AddableInt item) => new(Value + item.Value);
}

internal static class AssemblyPaths
{
    public static string ConsumptionTestsDir => GetMetadata("ConsumptionTestsDir");
    public static string RepoRootDir => GetMetadata("RepoRootDir");
    public static string BuildConfiguration => GetMetadata("BuildConfiguration");
    public static bool UsePackedPackage => bool.Parse(GetMetadata("UsePackedPackage"));
    public static string ResultfulPackageVersion => GetMetadata("ResultfulPackageVersion");
    public static string ResultfulPackageDir => GetMetadata("ResultfulPackageDir");

    private static string GetMetadata(string key) =>
        typeof(AssemblyPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == key).Value!;
}
