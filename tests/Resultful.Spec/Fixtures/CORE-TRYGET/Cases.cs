// M2 named methods (05): TryGetValue and TryGetError, the Dictionary.TryGetValue idiom.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void TryGetValue_and_TryGetError_agree_with_the_case()
    {
        Result<int, string> ok = 3;
        Result<int, string> failed = "bad";
        Check.True(ok.TryGetValue(out var v) && v == 3, "ok.TryGetValue");
        Check.False(ok.TryGetError(out _), "ok.TryGetError");
        Check.True(failed.TryGetError(out var e) && e == "bad", "failed.TryGetError");
        Check.False(failed.TryGetValue(out _), "failed.TryGetValue");
    }
}
