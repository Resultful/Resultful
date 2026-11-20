// Decision 17 (07): IErrorUnion ships in the core on every target (so the public API is the same everywhere), beside the
// shape interfaces the generator implements.
using Resultful;
using Resultful.Spec.Support;

public sealed record Leaf() : Error("leaf");
public readonly struct HandWritten(Error error) : IErrorUnion { public Error Error { get; } = error; }

public sealed class Shape(Result<int, Leaf> inner) : IResultShape<int, Leaf> { public Result<int, Leaf> AsResult() => inner; }

public static class Cases
{
    [SpecCase]
    public static void The_interfaces_are_in_the_core()
    {
        Check.Equal(typeof(Unit).Assembly, typeof(IErrorUnion).Assembly, "IErrorUnion");
        Check.Equal(typeof(Unit).Assembly, typeof(IValidationShape<,>).Assembly, "IValidationShape");
        IErrorUnion u = new HandWritten(new Leaf());
        Check.True(u.Error is Leaf, "usable on every target");
        Check.True(new Shape(new Leaf()).AsResult().IsFailure, "IResultShape");
    }
}
