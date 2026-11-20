// SHAPE-INTO (15): a core result with the same types, and another shape with the same success and error types, convert in;
// a different error hierarchy doesn't (CS0029) without a mapping.
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

[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct LoadOrderResult;

[ClosedHierarchy]
public abstract partial record PaymentError : Error;
public sealed record CardDeclined() : PaymentError("declined");

[ResultShape(typeof(PaymentError), Success = typeof(Order))]
public readonly partial struct PaymentResult;

public static class Cases
{
    static Result<Order, OrderError> Load(int id) => id > 0 ? new Order(id) : new OrderNotFound(id);
    static PlaceOrderResult Place(int id) => Load(id).Bind(o => Result.Ok<Order, OrderError>(o));
    static PlaceOrderResult FromShape(LoadOrderResult r) => r;
    static PlaceOrderResult FromOtherHierarchy(PaymentResult p) => p; // expect: CS0029

    [SpecCase]
    public static void Core_steps_and_sibling_shapes_return_straight_into_the_shape()
    {
        Check.True(Place(3).TryGetValue(out Order? o) && o.Id == 3, "from Result<T, TError>");
        Check.True(FromShape(new OrderNotFound(4)).TryGetError(out var e) && e is OrderNotFound, "from another shape");
    }
}
