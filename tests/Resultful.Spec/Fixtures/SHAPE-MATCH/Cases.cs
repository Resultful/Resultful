// SHAPE-MATCH (13b): Match calls exactly one arm, once: the success arm, or the error's most-derived leaf. Handlers are typed (RF0008)
// and come most-derived first, ties by fully qualified name (building-the-generator.md): OrderLocked before OrderNotFound.
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
    static (string Arm, int Calls) Run(PlaceOrderResult r)
    {
        var calls = 0;
        var arm = r.Match(
            (Order o) => { calls++; return "order"; },
            (OrderLocked l) => { calls++; return "locked"; },
            (OrderNotFound n) => { calls++; return "not found"; });
        return (arm, calls);
    }

    [SpecCase]
    public static void One_arm_runs_once()
    {
        Check.Equal(("order", 1), Run(new Order(1)), "success");
        Check.Equal(("not found", 1), Run(new OrderNotFound(1)), "leaf");
        Check.Equal(("locked", 1), Run(new OrderLocked(1, "ann").WithContext("note")), "leaf with context");
    }
}
