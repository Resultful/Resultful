// Quality (08b): only reading the value or error of a zero value throws. ToString, Equals and GetHashCode don't,
// so logging, dictionaries and the debugger survive the bug.
using System.Collections.Generic;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void The_zero_value_can_be_logged_hashed_and_compared()
    {
        var zero = default(Result<int, string>);
        Check.Equal("<default Result>", zero.ToString(), "ToString");
        Check.DoesNotThrow(() => zero.GetHashCode(), "GetHashCode");
        Check.True(zero.Equals(default(Result<int, string>)), "zero equals zero");
        Check.False(zero.Equals(Result.Ok<int, string>(0)), "zero is not Ok(0)");
        var set = new HashSet<Result<int>> { default, Result.Ok(1) };
        Check.Equal(2, set.Count, "usable as a key");
        Check.Equal("<default Result>", default(Result<int>).ToString(), "Result<T>");
    }
}
