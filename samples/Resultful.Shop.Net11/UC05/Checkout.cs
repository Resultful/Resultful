using Resultful.CodeAnalysis;

namespace Resultful.Shop.Net11;

public sealed record CardDeclined(string Reason) : Error($"card declined: {Reason}");
public sealed record ProviderUnavailable() : Error("the payment provider is unavailable");

public partial union PaymentError(CardDeclined, ProviderUnavailable);

// A nested union stays one case (decision 17): checkout handles every payment failure in one arm, then its own switch.
public partial union CheckoutError(OrderNotFound, PaymentError);

public sealed record Receipt(string PaymentId);

// A shape over the union (decision 17): its cases are the union's direct cases, so PaymentError stays one case.
[ResultShape(typeof(CheckoutError), Success = typeof(Receipt))]
public readonly partial struct CheckoutResult;

public static class Checkout
{
    // Each leaf converts into the shape, the nested union's leaves too.
    public static CheckoutResult Pay(int orderId, string card) => (orderId, card) switch
    {
        (<= 0, _) => new OrderNotFound(orderId),
        (_, "0000") => new CardDeclined("insufficient funds"),
        (_, "5000") => new ProviderUnavailable(),
        _ => new Receipt($"pay_{orderId}"),
    };

    // The generated Match with typed handlers (RF0008): one arm for every payment failure, then its own switch.
    public static string NextStep(CheckoutResult result) => result.Match(
        (Receipt r) => "show " + r.PaymentId,
        (OrderNotFound _) => "show the basket again",
        (PaymentError payment) => payment switch
        {
            CardDeclined => "ask for another card",
            ProviderUnavailable => "retry later",
        });

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
