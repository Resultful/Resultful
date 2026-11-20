// SHAPE-TRYGET (13b): TryGetValue and TryGetError agree with the case; on .NET 11 TryGetValue(out Leaf) is true only for that leaf.
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
    public static void TryGet_agrees_with_the_case()
    {
        PlaceOrderResult locked = new OrderLocked(2, "bo");
        Check.False(locked.TryGetValue(out Order? _), "not a success");
        Check.True(locked.TryGetError(out var e) && e is OrderLocked, "the error");
#if NET11_0_OR_GREATER
        Check.True(locked.TryGetValue(out OrderLocked? l) && l.By == "bo", "TryGetValue(out OrderLocked)");
        Check.False(locked.TryGetValue(out OrderNotFound? _), "not the other leaf");
#endif
    }
}
