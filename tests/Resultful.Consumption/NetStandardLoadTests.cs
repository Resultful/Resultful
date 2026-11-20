using System.Reflection;
using System.Runtime.Loader;
using OneOf;
using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// Loads the netstandard2.0 Resultful.dll asset in an isolated AssemblyLoadContext
/// and invokes core operations to confirm netstandard2.0 binary compatibility on net10.0 / net11.0.
/// </summary>
public sealed class NetStandardLoadTests
{
    [Fact]
    public void NetStandard20_asset_loads_and_executes_core_operations()
    {
        var netStandardDll = Path.Combine(
            AssemblyPaths.RepoRootDir,
            "src",
            "Resultful",
            "bin",
            AssemblyPaths.BuildConfiguration,
            "netstandard2.0",
            "Resultful.dll");

        // When running against a packed package without a local netstandard2.0 build, locate the package asset.
        if (!File.Exists(netStandardDll))
        {
            var currentDir = Path.GetDirectoryName(typeof(Result).Assembly.Location)!;
            var candidate = Path.GetFullPath(Path.Combine(currentDir, "..", "netstandard2.0", "Resultful.dll"));
            if (File.Exists(candidate))
            {
                netStandardDll = candidate;
            }
        }

        Assert.True(File.Exists(netStandardDll), $"Expected netstandard2.0 asset at {netStandardDll}");

        var alc = new NetStandardLoadContext(typeof(OneOfBase<,>).Assembly.Location);
        try
        {
            var asm = alc.LoadFromAssemblyPath(netStandardDll);
            var resultStatic = asm.GetType("Resultful.Result", throwOnError: true)!;
            var okMethod = resultStatic
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Ok" && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1)
                .MakeGenericMethod(typeof(int));

            var okResult = okMethod.Invoke(null, [42])!;
            var resultType = okResult.GetType();
            var mapMethod = resultType.GetMethod("Map")!.MakeGenericMethod(typeof(string));
            Func<int, string> mapFunc = x => $"v={x}";
            var mapped = mapMethod.Invoke(okResult, [mapFunc])!;

            var matchMethod = mapped.GetType().GetMethod("Match")!.MakeGenericMethod(typeof(string));
            Func<string, string> onOk = s => s;
            Func<IEnumerable<string>, string> onErr = errs => string.Join(",", errs);
            var output = (string)matchMethod.Invoke(mapped, [onOk, onErr])!;

            Assert.Equal("v=42", output);
        }
        finally
        {
            alc.Unload();
        }
    }

    private sealed class NetStandardLoadContext(string oneOfPath) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "OneOf", StringComparison.OrdinalIgnoreCase))
            {
                return LoadFromAssemblyPath(oneOfPath);
            }
            return null;
        }
    }
}
