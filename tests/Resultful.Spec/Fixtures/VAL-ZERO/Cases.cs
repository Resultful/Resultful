// Decision 10 (10): the zero Validation is invalid like every result type.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Reads_of_the_zero_validation_throw()
    {
        var zero = default(Validation<int, string>);
        Check.True(zero.IsDefault, "IsDefault");
        Check.Throws<System.InvalidOperationException>(() => _ = zero.Errors, "Errors");
        Check.Throws<System.InvalidOperationException>(() => zero.Match(v => v, es => es.Count), "Match");
        Check.Equal("<default Validation>", zero.ToString(), "ToString never throws");
        Check.True(typeof(Validation<int, string>).IsDefined(typeof(Resultful.CodeAnalysis.MustObserveAttribute), false), "[MustObserve]");
    }
}
