using System.Reflection;
using Resultful;
using Resultful.LINQ;
using Xunit;

namespace Resultful.Compatibility;

/// <summary>
/// A consumer of the packed package, on each runtime the Compat* targets in eng/build.cs cover. It checks the package's own asset is
/// the one loaded, then uses the library the way an application would.
/// </summary>
public sealed class ConsumerTests
{
#if NETFRAMEWORK || NET8_0
    private const string ExpectedAsset = ".NETStandard,Version=v2.0";
#elif NET10_0
    private const string ExpectedAsset = ".NETCoreApp,Version=v10.0";
#elif NET11_0
    private const string ExpectedAsset = ".NETCoreApp,Version=v11.0";
#endif

    [Fact]
    public void The_packed_package_and_the_asset_for_this_target_are_loaded()
    {
        var assembly = typeof(Result).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var expectedVersion = typeof(ConsumerTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ResultfulPackageVersion").Value!;

        // The informational version is "<package version>+<commit>".
        Assert.Equal(expectedVersion, informational.Split('+')[0]);
        Assert.Equal(ExpectedAsset, assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName);
    }

    [Fact]
    public void Results_map_bind_and_match()
    {
        var parsed = "42".Ok().Bind(Parse).Map(x => x * 2);
        Assert.Equal(84, parsed.Match(v => v, _ => -1));

        var failed = "forty".Ok().Bind(Parse).Map(x => x * 2);
        Assert.Equal("not a number: forty", failed.Match(_ => "", e => string.Join(",", e)));

        Result<int> Parse(string text) => int.TryParse(text, out var value) ? value.Ok() : Result.Fail<int>($"not a number: {text}");
    }

    [Fact]
    public void Typed_errors_and_void_results()
    {
        var typed = 5.Ok<int, string>().Bind(x => x > 3 ? "too big".Fail<int, string>() : x.Ok<int, string>());
        Assert.Equal("too big", typed.Match(_ => "", e => e));

        var steps = new List<string>();
        var sequence = Result.Sequence(
            () => { steps.Add("one"); return Result.Ok(); },
            () => { steps.Add("two"); return Result.Fail("stop"); },
            () => { steps.Add("three"); return Result.Ok(); });
        Assert.Equal(["one", "two"], steps);
        Assert.Equal(["stop"], sequence.Match(_ => Array.Empty<string>(), e => e.ToArray()));
    }

    [Fact]
    public async Task Async_overloads_and_linq()
    {
        var mapped = await 10.Ok().MapAsync(x => Task.FromResult(x + 1));
        Assert.Equal(11, mapped.Match(v => v, _ => -1));

        var query = from a in 2.Ok()
                    from b in 3.Ok()
                    select a * b;
        Assert.Equal(6, query.Match(v => v, _ => -1));
    }

    [Fact]
    public void Option_and_OneOf_interop()
    {
        Assert.Equal(7, 7.Some().Match(v => v, _ => -1));
        Assert.Equal("none", Option<int>.None.Match(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "none"));

        // Result<T> is built on OneOf, which the package depends on (3.0.271 or later).
        var oneOf = 3.Ok().ToOneOf();
        Assert.True(oneOf.IsT0);
    }

    [Fact]
    public void The_0_2_hazards_hold_on_this_runtime()
    {
        // default is a success, and a bare string converts to a failure (both fixed in 0.3).
        Assert.Equal(0, default(Result<int>).Match(v => v, _ => -1));
        Result<string> bare = "hello";
        Assert.Equal(["hello"], bare.Match(_ => Array.Empty<string>(), e => e.ToArray()));
    }
}
