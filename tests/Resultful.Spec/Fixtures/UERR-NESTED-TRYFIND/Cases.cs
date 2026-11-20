// Round 4 (13b): TryFind on a union forwards to the leaf, through its causes.
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

public static class Cases
{
    [SpecCase]
    public static void TryFind_searches_from_the_leaf()
    {
        OrderError e = (PaymentError)(new CardDeclined("expired") { Cause = new ProviderDown() });
        Check.True(e.TryFind<ProviderDown>(out _), "the leaf's cause");
        Check.True(e.TryFind<CardDeclined>(out var c) && c.Reason == "expired", "the leaf itself");
        Check.False(e.TryFind<OrderNotFound>(out _), "not there");
    }
}
