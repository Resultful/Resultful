// Decision 17 (13b): leaves of a nested union convert into the shape implicitly; a bare Result<T, OrderError> needs a cast.
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

public static class Cases
{
    static PlaceOrderResult Declined() => new CardDeclined("x");
    static PlaceOrderResult Missing() => new OrderNotFound(1);

    [SpecCase]
    public static void Every_leaf_converts()
    {
        Check.True(Declined().TryGetError(out var e) && e.Value is PaymentError { Value: CardDeclined }, "nested leaf");
        Check.True(Missing().TryGetError(out var m) && m.Value is OrderNotFound, "outer leaf");
    }
}
