// Decision 1 (11): Option<T> uses Resultful.None, not OneOf.Types.None.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Option_matches_with_Resultful_None()
    {
        Option<int> none = Option<int>.None;
        var text = none.Match(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture), (None _) => "none");
        Check.Equal("none", text, "match");
        Option<int> fromNone = None.Value;
        Check.Equal("none", fromNone.Match(_ => "some", (None _) => "none"), "implicit from None");
    }
}
