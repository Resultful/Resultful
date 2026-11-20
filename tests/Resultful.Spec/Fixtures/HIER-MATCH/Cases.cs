// HIER-MATCH (13b): a base's generated Match and Switch reach every leaf across nested bases, most-derived first.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

[ClosedHierarchy]
public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
[ClosedHierarchy]
public abstract partial record PaymentFault : OrderError;
public sealed record CardDeclined(string Reason) : PaymentFault($"card declined: {Reason}");

public static class Cases
{
    [SpecCase]
    public static void Every_leaf_is_reached()
    {
        OrderError e = new CardDeclined("expired");
        Check.Equal(402, e.Match((CardDeclined c) => 402, (OrderNotFound n) => 404), "nested leaf");
        var seen = "";
        ((OrderError)new OrderNotFound(1)).Switch((CardDeclined c) => seen = "card", (OrderNotFound n) => seen = "nf");
        Check.Equal("nf", seen, "Switch");
    }
}
