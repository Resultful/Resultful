// Decision 14 on decision 4 (12): Sequence and Check chains take Result<Unit> steps.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Sequence_stops_at_the_first_failure()
    {
        var ran = new System.Collections.Generic.List<string>();
        Result<Unit> Step(string name, bool ok) { ran.Add(name); return ok ? Result.Ok() : Result.Fail(name + " failed"); }

        var r = Result.Sequence(() => Step("a", true), () => Step("b", false), () => Step("c", true));
        Check.True(r.TryGetError(out var e) && e.Message == "b failed", "first failure");
        Check.SequenceEqual(new[] { "a", "b" }, ran, "c never ran");
    }

    [SpecCase]
    public static void Check_keeps_the_value()
    {
        var r = Result.Ok(10).Check(v => v > 5 ? Result.Ok() : Result.Fail("too small"));
        Check.Equal(10, r.Value, "value kept");
    }
}
