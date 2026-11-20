// SHAPE-CONTEXT (15): WithContext on a failed shape returns the same shape type, and the error keeps its leaf type.
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
    public static void Context_keeps_the_shape()
    {
        PlaceOrderResult r = new OrderNotFound(5);
        PlaceOrderResult noted = r.WithContext("placing order 5");
        Check.True(noted.TryGetError(out var e) && e is OrderNotFound && e.Context[0] == "placing order 5", "same leaf, with the note");
    }
}
