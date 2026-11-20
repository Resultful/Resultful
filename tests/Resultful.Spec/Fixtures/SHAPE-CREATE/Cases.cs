// SHAPE-CREATE (13b): Ok, Fail and the implicit conversions from the success type and from every leaf build the right case.
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
    public static void Each_way_of_building_gives_the_right_case()
    {
        PlaceOrderResult fromValue = new Order(1);
        PlaceOrderResult fromLeaf = new OrderLocked(1, "ann");
        Check.True(fromValue.TryGetValue(out Order? o) && o.Id == 1, "from the success type");
        Check.True(fromLeaf.TryGetError(out var e) && e is OrderLocked, "from a leaf");
        Check.True(PlaceOrderResult.Ok(new Order(2)).TryGetValue(out Order? _), "Ok");
        Check.True(PlaceOrderResult.Fail(new OrderNotFound(3)).TryGetError(out var nf) && nf is OrderNotFound { Id: 3 }, "Fail");
    }
}
