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
    public static string TargetFramework => GetMetadata("TargetFramework");
    public static bool UsePackedPackage => bool.Parse(GetMetadata("UsePackedPackage"));
    public static string ResultfulPackageVersion => GetMetadata("ResultfulPackageVersion");
    public static string ResultfulPackageDir => GetMetadata("ResultfulPackageDir");

    private static string GetMetadata(string key) =>
        typeof(AssemblyPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == key).Value!;
}

/// <summary>Reads the error side of a result, or a marker when it succeeded, so failure paths assert in one line.</summary>
internal static class Outcome
{
    public const string Succeeded = "<ok>";

    public static string[] Errors<T>(this Result<T> result) => result.Match(_ => [Succeeded], e => e.ToArray());

    public static string[] Errors(this VoidResult result) => result.Match(_ => [Succeeded], e => e.ToArray());

    public static string Error<T>(this Result<T, TestError> result) => result.Match(_ => Succeeded, e => e.Message);

    public static string Error(this VoidResult<TestError> result) => result.Match(_ => Succeeded, e => e.Message);

    /// <summary>Yields <paramref name="items"/> lazily and records how many were pulled.</summary>
    public static IEnumerable<T> Counted<T>(IEnumerable<T> items, Action pulled)
    {
        foreach (var item in items)
        {
            pulled();
            yield return item;
        }
    }
}
