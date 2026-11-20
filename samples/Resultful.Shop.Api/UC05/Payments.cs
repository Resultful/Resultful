using Resultful.AspNetCore;
using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

internal sealed record ProviderResponse(string Code, string Id, string Message);

// The provider: card "0000" is declined, "6666" looks like fraud, "5000" times out, anything else is charged.
internal sealed class PaymentProvider
{
    public ProviderResponse Charge(decimal total, string card) => card switch
    {
        "0000" => new ProviderResponse("card_declined", "", "insufficient funds"),
        "6666" => new ProviderResponse("fraud", "", "risk score 97"),
        "5000" => new ProviderResponse("timeout", "", "upstream 504"),
        _ => new ProviderResponse("ok", $"pay_{total:0}_{card}", ""),
    };
}

internal sealed class Payments
{
    private readonly PaymentProvider _provider = new();

    #region snippet: uc05-adapter
    // the adapter: the only code that knows the provider's codes
    public Result<PaymentId, PaymentError> Charge(Order o)
    {
        var response = _provider.Charge(o.Total, o.Card);
        return response.Code switch
        {
            "ok" => new PaymentId(response.Id),
            "card_declined" => new CardDeclined(response.Message),
            "fraud" => new FraudSuspected(),
            _ => new ProviderUnavailable(),
        };
    }

    // the checkout: decides what to do, and must decide for every case
    public static CheckoutStep Next(PaymentError error) => error switch
    {
        CardDeclined => CheckoutStep.AskForAnotherCard,
        ProviderUnavailable => CheckoutStep.RetryLater,
        FraudSuspected => CheckoutStep.ContactSupport,
    };
    #endregion
}

internal static class PaymentEndpoints
{
    // A declined card is the customer's to fix (409); the provider being down and fraud stay 500s, so the
    // provider's words never reach the client.
    public static void MapErrors(ResultfulHttpOptions http) => http.Map<CardDeclined>(StatusCodes.Status409Conflict);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/payments", (PayRequest r, Payments payments) =>
            payments.Charge(new Order(r.OrderId, "checkout", r.Total, r.Card)).ToHttpResult());
        app.MapPost("/checkout/next", (PayRequest r, Payments payments) =>
            payments.Charge(new Order(r.OrderId, "checkout", r.Total, r.Card))
                    .Match(_ => CheckoutStep.Done, Payments.Next));
    }
}
