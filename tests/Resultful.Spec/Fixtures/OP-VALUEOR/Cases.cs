// Decision 9 (18): `result | value` unwraps with a default (ValueOr), giving a T.
using Resultful;
using Resultful.Operators;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Pipe_with_a_value_is_ValueOr()
    {
        int fallback = Result.Fail<int>("x") | 42;
        Check.Equal(42, fallback, "failure");
        int value = Result.Ok(1) | 42;
        Check.Equal(1, value, "success");
    }
}
