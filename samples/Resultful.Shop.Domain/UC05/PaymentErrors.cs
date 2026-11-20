using Resultful.CodeAnalysis;

namespace Resultful.Shop.Domain;

// Use case 5: a payment provider, translated at the adapter.
#region snippet: uc05-errors
[ClosedHierarchy]
public abstract partial record PaymentError : Error;
// Not the docs' CardDeclined(string Reason): a mapped 4xx shows Message to the client, so the provider's own words
// stay in the adapter's log (DECISIONS-TAKEN, 04d review).
public sealed record CardDeclined() : PaymentError("The card was declined.");
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
