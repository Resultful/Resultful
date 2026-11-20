// Decision 9 (18): `|` falls back; the first success wins. One `using` opts in.
using Resultful;
using Resultful.Operators;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Pipe_is_OrElse()
    {
        Result<int, string> bad = "bad";
        Result<int, string> good = 2;
        Check.Equal(2, (bad | good).Value, "bad | good");
        Check.Equal(bad.OrElse(good), bad | good, "the named method");
        Check.Equal(5, (Result.Ok(5) | Result.Ok(6)).Value, "first success");
    }
}
