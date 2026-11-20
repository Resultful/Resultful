// Decision 9 as changed by decision 14 (18): `&` combines two Validations into a tuple, keeping every error.
using Resultful;
using Resultful.Operators;
using Resultful.Spec.Support;

public static class Cases
{
    static Validation<string> Name(string? v) => v is null ? Error.From("name is required") : v;
    static Validation<int> Age(int v) => v < 0 ? Error.From("age is negative") : v;

    [SpecCase]
    public static void Ampersand_is_Combine()
    {
        var bad = Name(null) & Age(-1);
        Check.SequenceEqual(new[] { "name is required", "age is negative" }, System.Linq.Enumerable.Select(bad.Errors, e => e.Message), "both errors");
        var good = Name("ann") & Age(3);
        Check.Equal(("ann", 3), good.Value, "tuple");
    }
}
