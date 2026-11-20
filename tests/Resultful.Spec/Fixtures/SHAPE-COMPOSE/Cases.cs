// SHAPE-COMPOSE (13b): Map and Bind on a shape give the same result as on AsResult().
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
    public static void Map_and_Bind_agree_with_the_core()
    {
        foreach (PlaceOrderResult shape in new PlaceOrderResult[] { new Order(2), new OrderNotFound(2) })
        {
            Check.Equal(shape.AsResult().Map(o => o.Id * 10), shape.Map(o => o.Id * 10), "Map");
            Check.Equal(shape.AsResult().Bind(o => Result.Ok<int, OrderError>(o.Id)), shape.Bind(o => Result.Ok<int, OrderError>(o.Id)), "Bind");
        }
    }
}
