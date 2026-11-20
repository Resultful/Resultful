namespace Resultful.Shop.Net11;

public sealed record CardDeclined(string Reason) : Error($"card declined: {Reason}");
public sealed record ProviderUnavailable() : Error("the payment provider is unavailable");

public partial union PaymentError(CardDeclined, ProviderUnavailable);

// A nested union stays one case (decision 17): checkout handles every payment failure in one arm, then its own switch.
public partial union CheckoutError(OrderNotFound, PaymentError);

public static class Checkout
{
    public static string Next(CheckoutError error) => error switch
    {
        OrderNotFound => "show the basket again",
        PaymentError payment => payment switch
        {
            CardDeclined => "ask for another card",
            ProviderUnavailable => "retry later",
        },
    };

    // IErrorUnion.Error reaches the innermost leaf, so the log line names what actually failed.
    public static string Describe(CheckoutError error) => error.Error.Message;
}
