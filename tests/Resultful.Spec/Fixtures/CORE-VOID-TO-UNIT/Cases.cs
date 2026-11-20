// M2 (10): VoidResult converts to Result<Unit> and back, so code moves off it one call at a time.
using Resultful;
using Resultful.Spec.Support;

// VoidResult is obsolete from 12; this fixture is about the bridge 10 adds, so it must not depend on that.
#pragma warning disable CS0618
public static class Cases
{
    [SpecCase]
    public static void VoidResult_and_Result_of_Unit_convert_both_ways()
    {
        VoidResult legacy = Result.Fail("old");
        Result<Unit> modern = legacy;
        Check.True(modern.TryGetError(out var e) && e.Message == "old", "VoidResult to Result<Unit>");
        VoidResult back = modern;
        Check.True(back.Match(_ => false, _ => true), "and back");
    }
}
