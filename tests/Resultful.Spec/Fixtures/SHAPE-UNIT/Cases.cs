// SHAPE-UNIT (13b): a shape with no Success succeeds with Unit; on .NET 11 its switch has a Unit arm.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

[ClosedHierarchy]
public abstract partial record OrderError : Error;
public sealed record OrderLocked(int Id) : OrderError($"order {Id} is locked");

[ResultShape(typeof(OrderError))]
public readonly partial struct CancelOrderResult;

public static class Cases
{
    static CancelOrderResult Cancel(bool locked) => locked ? new OrderLocked(1) : Unit.Value;

    [SpecCase]
    public static void A_valueless_shape_succeeds_with_Unit()
    {
        Check.True(Cancel(false).TryGetValue(out Unit? u) && ReferenceEquals(u, Unit.Value), "Unit");
        Check.True(CancelOrderResult.Ok().TryGetValue(out Unit? _), "Ok");
        Check.True(Cancel(true).TryGetError(out _), "failure");
#if NET11_0_OR_GREATER
        Check.Equal("done", Cancel(false) switch { Unit => "done", OrderLocked => "locked" }, "Unit arm");
#endif
    }
}
