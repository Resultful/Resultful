// Decision 17 (13b): a nested union stays one case. The shape's cases are OrderNotFound and PaymentError;
// `PaymentError p =>` is an exhaustive arm, a flat arm for its leaf is CS8121, and the inner switch needs every inner leaf.
// The twin is a grouped switch over an intermediate [ClosedHierarchy] base (decision 16 item 9).
// spec: tfm net11.0
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record CardDeclined(string Reason) : Error($"card declined: {Reason}");
public sealed record ProviderDown() : Error("the payment provider is down");
public sealed record Order(int Id);
public partial union PaymentError(CardDeclined, ProviderDown);
public partial union OrderError(OrderNotFound, PaymentError);

[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

namespace Twin
{
    [ClosedHierarchy] public abstract partial record OrderError : Error;
    public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
    [ClosedHierarchy] public abstract partial record PaymentError : OrderError;
    public sealed record CardDeclined(string Reason) : PaymentError($"card declined: {Reason}");
    public sealed record ProviderDown() : PaymentError("the payment provider is down");
    [ResultShape(typeof(OrderError), Success = typeof(global::Order))] public readonly partial struct PlaceOrderResult;
}

public static class Cases
{
    static string U(PlaceOrderResult r) => r switch
    {
        Order => "201",
        OrderNotFound => "404",
        PaymentError p => p switch { CardDeclined => "402 declined", ProviderDown => "503" },
    };

    static string H(Twin.PlaceOrderResult r) => r switch
    {
        Order => "201",
        Twin.OrderNotFound => "404",
        Twin.PaymentError p => p switch { Twin.CardDeclined => "402 declined", Twin.ProviderDown => "503", _ => "?" },
    };

    static string DropsInner(PaymentError p) => p switch { CardDeclined => "402" }; // expect: CS8509

    [SpecCase]
    public static void The_inner_union_is_handled_in_its_own_switch()
    {
        Check.Equal(H(new Twin.CardDeclined("x")), U((PaymentError)new CardDeclined("x")), "inner leaf");
        Check.Equal(H(new Twin.ProviderDown()), U((PaymentError)new ProviderDown()), "other inner leaf");
        Check.Equal(H(new Twin.OrderNotFound(1)), U(new OrderNotFound(1)), "outer leaf");
    }
}
