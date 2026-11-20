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
    public static void The_twin_finds_the_same_things()
    {
        OrderError union = (PaymentError)(new CardDeclined("expired") { Cause = new ProviderDown() });
        Twin.OrderError twin = new Twin.CardDeclined("expired") { Cause = new Twin.ProviderDown() };
        Check.Equal(twin.TryFind<Twin.ProviderDown>(out _), union.TryFind<ProviderDown>(out _), "the cause");
        Check.Equal(twin.TryFind<Twin.OrderNotFound>(out _), union.TryFind<OrderNotFound>(out _), "absent");
    }
}
