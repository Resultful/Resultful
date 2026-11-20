// Round 4 (13b): for a nested union, the generated IErrorUnion.Error unwraps the inner union to the leaf.
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
    public static void Error_reaches_the_innermost_leaf()
    {
        OrderError e = (PaymentError)new CardDeclined("expired");
        Check.True(((IErrorUnion)e).Error is CardDeclined { Reason: "expired" }, "IErrorUnion.Error");
        Check.Equal("card declined: expired", e.Message, "Message forwards to the leaf");
        Check.Equal(null, e.Cause, "Cause forwards to the leaf");
    }
}
