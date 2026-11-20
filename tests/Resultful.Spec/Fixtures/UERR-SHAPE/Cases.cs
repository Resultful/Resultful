// Decision 17 (13b): [ResultShape] accepts a union; the generator reads its cases as the leaf list and the switch is flat.
// A missing arm is CS8509. The [ClosedHierarchy] twin gives the same output.
// spec: tfm net11.0
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");
public sealed record Order(int Id);

public partial union OrderError(OrderNotFound, OrderLocked);

[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

namespace Twin
{
    [ClosedHierarchy] public abstract partial record OrderError : Error;
    public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
    public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
    [ResultShape(typeof(OrderError), Success = typeof(global::Order))]
    public readonly partial struct PlaceOrderResult;
}

public static class Cases
{
    static string U(PlaceOrderResult r) => r switch { Order o => "201", OrderNotFound => "404", OrderLocked l => "409 " + l.By };
    static string H(Twin.PlaceOrderResult r) => r switch { Order o => "201", Twin.OrderNotFound => "404", Twin.OrderLocked l => "409 " + l.By };
    static string Missing(PlaceOrderResult r) => r switch { Order o => "201", OrderNotFound => "404" }; // expect: CS8509

    [SpecCase]
    public static void Flat_switch_over_a_union_shape_matches_the_twin()
    {
        Check.Equal(H(new Order(1)), U(new Order(1)), "success");
        Check.Equal(H(new Twin.OrderLocked(1, "bo")), U(new OrderLocked(1, "bo")), "leaf");
        Check.Equal(H(new Twin.OrderNotFound(2)), U(new OrderNotFound(2)), "other leaf");
    }
}
