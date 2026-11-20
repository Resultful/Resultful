// M2 named methods (05): var (ok, value, error) = result.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void A_result_deconstructs()
    {
        Result<int, string> ok = 5;
        var (isSuccess, value, error) = ok;
        Check.True(isSuccess, "isSuccess");
        Check.Equal(5, value, "value");
        Check.Equal(null, error, "error");

        Result<int, string> failed = "no";
        var (s2, _, e2) = failed;
        Check.False(s2, "isSuccess");
        Check.Equal("no", e2, "error");
    }
}
