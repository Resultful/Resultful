// Decision 9 (18): `>>` binds, with one type-changing form; a lambda on the right needs parentheses.
using System.Globalization;
using Resultful;
using Resultful.Operators;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Shift_is_Bind()
    {
        Result<int> a = Result.Ok(10);
        Result<string> s = a >> (x => x > 5 ? Result.Ok(x.ToString(CultureInfo.InvariantCulture)) : Result.Fail<string>("small"));
        Check.Equal("10", s.Value, "type-changing");
        Result<int, string> typed = 3;
        Check.Equal(6, (typed >> (x => Result.Ok<int, string>(x * 2))).Value, "Result<T, TError>");
        Check.Equal(a.Bind(x => Result.Ok(x + 1)), a >> (x => Result.Ok(x + 1)), "the named method");
    }
}
