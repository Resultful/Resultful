// Round 4 (08b): a hand-written IUnion struct holding a long isn't read as a reference; Fail doesn't crash and keeps it.
// spec: tfm net11.0
using Resultful;
using Resultful.Spec.Support;

public readonly struct StatusCode(long value) : System.Runtime.CompilerServices.IUnion
{
    public long Number { get; } = value;
    public object? Value => Number;
}

public static class Cases
{
    [SpecCase]
    public static void A_custom_union_takes_the_IUnion_path()
    {
        var r = Result.Fail<int, StatusCode>(new StatusCode(0x7FFF_0000_0000_0001));
        Check.True(r.TryGetError(out var e) && e.Number == 0x7FFF_0000_0000_0001, "kept, not misread");
    }
}
