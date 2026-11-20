// Quality (06): errors print the same under tr-TR (dotted and dotless i) as under the invariant culture.
using System.Globalization;
using Resultful;
using Resultful.Spec.Support;

public sealed record InvalidId(int Id) : Error($"invalid id {Id}");

public static class Cases
{
    [SpecCase]
    public static void ToString_doesnt_depend_on_the_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            var error = new InvalidId(1500).WithContext("ITEM import");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = error.ToString();
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Check.Equal(invariant, error.ToString(), "tr-TR");
            Check.True(invariant.Contains("InvalidId", System.StringComparison.Ordinal), invariant);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
