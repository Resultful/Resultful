// spec: tfm net11.0
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

namespace Twin
{
    // The [ClosedHierarchy] twin: PaymentError is an intermediate closed base, as the union nests PaymentError.
    [ClosedHierarchy] public abstract partial record OrderError : Error;
    public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
    [ClosedHierarchy] public abstract partial record PaymentError : OrderError;
    public sealed record CardDeclined(string Reason) : PaymentError($"card declined: {Reason}");
    public sealed record ProviderDown() : PaymentError("the payment provider is down");
    [ResultShape(typeof(OrderError), Success = typeof(global::Order))] public readonly partial struct PlaceOrderResult;
}

public static class TwinCases
{
    [SpecCase]
    public static void The_twin_widens_to_the_same_leaf()
    {
        Result<Order> union = (PlaceOrderResult)new CardDeclined("expired");
        Result<Order> twin = (Twin.PlaceOrderResult)new Twin.CardDeclined("expired");
        Check.Equal(twin.Error.GetType().Name, union.Error.GetType().Name, "leaf type");
        Check.Equal(twin.Error.Message, union.Error.Message, "message");
    }
}
