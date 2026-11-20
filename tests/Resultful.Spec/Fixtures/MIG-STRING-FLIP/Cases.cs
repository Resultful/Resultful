// Migration hazard (09): `Result<string> r = "message"` was a failure in 0.2 and is a success in 0.3 (RF0004 flags it).
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void A_bare_string_is_now_the_value()
    {
        Result<string> r = "message";
        Check.True(r.IsSuccess && r.Value == "message", "success");
    }
}
