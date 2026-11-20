// Decision 9 (18): `||` falls back lazily: the right side runs only on a failure.
using Resultful;
using Resultful.Operators;
using Resultful.Spec.Support;

public static class Cases
{
    static int calls;
    static Result<int> Expensive() { calls++; return Result.Ok(9); }

    [SpecCase]
    public static void The_right_side_runs_only_when_needed()
    {
        calls = 0;
        var a = Result.Ok(1) || Expensive();
        Check.Equal(0, calls, "not called after a success");
        Check.Equal(1, a.Value, "left");
        var b = Result.Fail<int>("x") || Expensive();
        Check.Equal(1, calls, "called after a failure");
        Check.Equal(9, b.Value, "right");
    }
}
