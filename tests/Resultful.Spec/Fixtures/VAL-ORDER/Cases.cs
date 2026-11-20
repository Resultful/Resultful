// Decision 14 law 2 (10): combining keeps every error, in order, however it's grouped.
using System.Linq;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static Validation<int, string> F(string e) => e;

    [SpecCase]
    public static void Grouping_doesnt_change_the_errors()
    {
        var left = Validation.Combine(Validation.Combine(F("a"), F("b")), F("c"));
        var right = Validation.Combine(F("a"), Validation.Combine(F("b"), F("c")));
        Check.SequenceEqual(new[] { "a", "b", "c" }, left.Errors, "(a & b) & c");
        Check.SequenceEqual(left.Errors, right.Errors, "a & (b & c)");
    }

    [SpecCase]
    public static void A_successful_side_adds_nothing()
    {
        var v = Validation.Combine(Validation.Ok<int, string>(1), F("b"), F("c"), (x, y, z) => x + y + z);
        Check.SequenceEqual(new[] { "b", "c" }, v.Errors.ToArray(), "errors");
    }
}
