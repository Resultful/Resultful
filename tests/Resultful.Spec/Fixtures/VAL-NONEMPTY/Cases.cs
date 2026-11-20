// Decision 14 law 1 (10): a failed Validation is never empty; Fail with no errors throws.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Fail_with_no_errors_throws()
    {
        Check.Throws<System.ArgumentException>(() => Validation.Fail<int, string>(System.Array.Empty<string>()), "Fail<T, TError>(empty)");
        Check.Throws<System.ArgumentException>(() => Validation.Fail<int>(System.Array.Empty<Error>()), "Fail<T>(empty)");
    }

    [SpecCase]
    public static void A_failure_has_at_least_one_error()
    {
        var v = Validation.Fail<int, string>(new[] { "x" });
        Check.True(v.TryGetErrors(out var errors) && errors.Count == 1, "one error");
    }
}
