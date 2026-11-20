// SHAPE-NOALLOC (13b): building, Match with static lambdas, TryGetValue and widening allocate nothing.
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
    static readonly Order Order1 = new(1);
    static readonly OrderLocked Locked = new(1, "ann");

    [SpecCase]
    public static void The_hot_path_allocates_nothing()
    {
        Check.Equal(0L, Check.Allocated(() => { PlaceOrderResult r = Order1; _ = r.TryGetValue(out Order? _); }), "build and TryGetValue");
        Check.Equal(0L, Check.Allocated(() => { PlaceOrderResult r = Locked; _ = r.Match(static (Order o) => 1, static (OrderLocked l) => 3, static (OrderNotFound n) => 2); }), "Match");
        Check.Equal(0L, Check.Allocated(() => { PlaceOrderResult r = Order1; Result<Order, OrderError> w = r; _ = w.IsSuccess; }), "widening");
    }
}
