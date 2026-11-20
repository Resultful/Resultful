// Decision 17 (15): a shape whose error is a union widens implicitly to Result<T> through IErrorUnion.Error (the leaf).
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
    [SpecCase]
    public static void Widening_to_Result_of_T_gives_the_leaf()
    {
        PlaceOrderResult r = new CardDeclined("expired");
        Result<Order> widened = r;
        Check.True(widened.TryGetError(out var e) && e is CardDeclined { Reason: "expired" }, "the leaf");
        Result<Order> ok = (PlaceOrderResult)new Order(2);
        Check.Equal(2, ok.Value.Id, "success");
    }
}
