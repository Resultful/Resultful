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
    public static void The_twin_gets_the_same_note()
    {
        OrderError union = ((OrderError)(PaymentError)new CardDeclined("expired")).WithContext("charging order 3");
        Twin.OrderError twin = ((Twin.OrderError)new Twin.CardDeclined("expired")).WithContext("charging order 3");
        Check.SequenceEqual(twin.Context, ((IErrorUnion)union).Error.Context, "context");
        Check.Equal(twin is Twin.PaymentError, union.Value is PaymentError, "still grouped under PaymentError");
    }
}
