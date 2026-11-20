// M2 named methods (05): OrElse (eager and lazy) and ValueOr, the named forms of `|` and `||`.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void The_first_success_wins()
    {
        Result<int, string> bad = "bad";
        Result<int, string> good = 2;
        Check.Equal(2, bad.OrElse(good).Value, "bad.OrElse(good)");
        Check.Equal(2, good.OrElse(Result.Ok<int, string>(9)).Value, "good.OrElse(other)");
    }

    [SpecCase]
    public static void The_lazy_form_runs_only_on_a_failure_and_sees_the_error()
    {
        var calls = 0;
        Result<int, string> good = 1;
        good.OrElse(e => { calls++; return 0; });
        Check.Equal(0, calls, "not called on a success");
        Result<int, string> bad = "bad";
        Check.Equal(3, bad.OrElse(e => e.Length).Value, "called with the error");
    }

    [SpecCase]
    public static void ValueOr_unwraps_with_a_fallback()
    {
        Check.Equal(42, Result.Fail<int>("x").ValueOr(42), "failure");
        Check.Equal(1, Result.Ok(1).ValueOr(42), "success");
    }
}
