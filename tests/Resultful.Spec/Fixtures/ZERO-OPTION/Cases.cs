// Decision 10 (08b): the zero value of Option<T> is None, a safe meaning, so it doesn't throw.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void The_zero_Option_is_None()
    {
        var text = default(Option<int>).Match(v => "some " + v, (None _) => "none");
        Check.Equal("none", text, "default(Option<int>)");
    }
}
