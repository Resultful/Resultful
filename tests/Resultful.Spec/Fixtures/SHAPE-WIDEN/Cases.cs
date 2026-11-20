// SHAPE-WIDEN (13b): AsResult and the implicit conversions to Result<T, TError> and Result<T> keep the value or the same error instance.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

[ClosedHierarchy]
public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
public sealed record Order(int Id);

[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

public static class Cases
{
    [SpecCase]
    public static void Widening_keeps_the_instance_and_round_trips()
    {
        var locked = new OrderLocked(1, "ann");
        PlaceOrderResult shape = locked;
        Result<Order, OrderError> core = shape;
        Result<Order> general = shape;
        Check.True(core.TryGetError(out var e1) && ReferenceEquals(e1, locked), "Result<T, TError>");
        Check.True(general.TryGetError(out var e2) && ReferenceEquals(e2, locked), "Result<T>");
        Check.Equal(core, shape.AsResult(), "AsResult");
        PlaceOrderResult back = core;
        Check.True(back.TryGetError(out var e3) && ReferenceEquals(e3, locked), "round trip");
    }
}
