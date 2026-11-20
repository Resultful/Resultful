// Decision 10 (08b): IsDefault reports the zero value and never throws.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void IsDefault_is_true_only_for_the_zero_value()
    {
        Check.True(default(Result<int>).IsDefault, "default Result<int>");
        Check.True(default(Result<int, string>).IsDefault, "default Result<int, string>");
        Check.False(Result.Ok(0).IsDefault, "Ok(0)");
        Check.False(Result.Fail<int>("x").IsDefault, "Fail");
    }
}
