using System.Globalization;
using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// eng/run-tests.cs runs this suite again under each culture in the project's TestCultures, set for the whole run
/// (xunit.v3's -culture), and names it in RESULTFUL_TEST_CULTURE. This checks the culture really took effect, so
/// a run labelled tr-TR can't silently use the machine's culture.
/// </summary>
public sealed class CultureTests
{
    [Fact]
    public void The_run_uses_the_culture_the_runner_asked_for()
    {
        var requested = Environment.GetEnvironmentVariable("RESULTFUL_TEST_CULTURE");
        if (string.IsNullOrEmpty(requested))
        {
            return; // the machine's own culture: nothing was asked for
        }

        var expected = requested == "invariant" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(requested);
        Assert.Equal(expected.Name, CultureInfo.CurrentCulture.Name);
        Assert.Equal(expected.Name, CultureInfo.CurrentUICulture.Name);
        if (requested == "tr-TR")
        {
            Assert.Equal("İ", "i".ToUpper(CultureInfo.CurrentCulture)); // ICU data is present, so tr-TR is the real culture
        }
    }
}
