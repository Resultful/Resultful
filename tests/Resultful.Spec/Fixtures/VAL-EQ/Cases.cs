// Round 4 (10): Validation and ValidationFailed compare their errors element by element.
using Resultful;
using Resultful.Spec.Support;

public sealed record Required(string Field) : Error($"{Field} is required");

public static class Cases
{
    [SpecCase]
    public static void Two_failures_with_the_same_errors_are_equal()
    {
        var a = Validation.Fail<int, string>(new[] { "x", "y" });
        var b = Validation.Fail<int, string>(new System.Collections.Generic.List<string> { "x", "y" });
        Check.True(a == b, "==");
        Check.Equal(a.GetHashCode(), b.GetHashCode(), "hash");
        Check.False(a == Validation.Fail<int, string>(new[] { "y", "x" }), "order matters");
        Check.True(Validation.Ok<int, string>(1).Equals(Validation.Ok<int, string>(1)), "successes");
    }

    [SpecCase]
    public static void ValidationFailed_compares_its_list()
    {
        var a = new ValidationFailed<Required>(new[] { new Required("a") });
        var b = new ValidationFailed<Required>(new System.Collections.Generic.List<Required> { new("a") });
        Check.Equal(a, b, "same errors, different lists");
        Check.Equal(a.GetHashCode(), b.GetHashCode(), "hash");
    }
}
