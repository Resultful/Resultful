using Resultful.Compatibility.UpgradeLibrary;
using Xunit;

namespace Resultful.Compatibility;

public sealed class UpgradeTests
{
    [Fact]
    public void A_library_compiled_against_beta05_runs_on_the_packed_package()
    {
        // The library references Resultful, Version=1.0.0.0; .NET Core won't bind it to a lower assembly version.
        Assert.Equal(new Version(1, 0, 0, 0), typeof(Result).Assembly.GetName().Version);
        Assert.Equal(42, Parser.Parse("42").Match(v => v, _ => -1));
        Assert.Equal(["not a number: x"], Parser.Parse("x").Match(_ => Array.Empty<string>(), e => e.ToArray()));
    }
}
