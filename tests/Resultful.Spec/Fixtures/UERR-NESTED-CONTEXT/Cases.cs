// Round 4 (13b): WithContext on a nested union dispatches per direct case and rewraps, keeping the inner union's type.
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
    public static void Context_keeps_both_union_types()
    {
        OrderError e = (PaymentError)new CardDeclined("expired");
        OrderError noted = e.WithContext("charging order 3");
        Check.True(noted.Value is PaymentError { Value: CardDeclined }, "still PaymentError holding CardDeclined");
        Check.SequenceEqual(new[] { "charging order 3" }, ((IErrorUnion)noted).Error.Context, "the note is on the leaf");
    }
}
