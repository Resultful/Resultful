using Resultful.CodeAnalysis;

namespace Resultful.Shop.Domain;

// Use case 5: a payment provider, translated at the adapter.
#region snippet: uc05-errors
[ClosedHierarchy]
public abstract partial record PaymentError : Error;
public sealed record CardDeclined(string Reason) : PaymentError($"card declined: {Reason}");
public sealed record ProviderUnavailable() : PaymentError("the payment provider is unavailable");
public sealed record FraudSuspected() : PaymentError("payment blocked");
#endregion

public readonly record struct PaymentId(string Value);

public sealed record PayRequest(int OrderId, decimal Total, string Card);

public enum CheckoutStep
{
    Done,
    AskForAnotherCard,
    RetryLater,
    ContactSupport,
}
