using System.Text.Json;
using Resultful.Testing.Json;

namespace Resultful.Text.Json.Tests;

// A checkout as an app would write it: validate the request, look the order up, charge the card, then send the result
// over the wire and read it back on the other side. Each stage's outcome is pinned as one JSON literal.
public class ScenarioTests
{
    private sealed record CheckoutRequest(int OrderId, string Card);

    private sealed record Receipt(int OrderId, decimal Total);

    private static readonly Dictionary<int, (string? LockedBy, decimal Total)> Orders = new()
    {
        [1] = (null, 12.5m),
        [2] = ("ann", 3m),
        [3] = (null, 99m),
    };

    private static Validation<int> OrderId(int id) => id > 0 ? Validation.Ok(id) : Validation.Fail<int>(Error.From("the order id must be positive"));

    private static Validation<string> Card(string card) =>
        card.Length == 4 ? Validation.Ok(card) : Validation.Fail<string>(new InvalidItem(card.Length));

    private static Result<decimal> Load(int id) =>
        !Orders.TryGetValue(id, out var order) ? Result.Fail<decimal>(new OrderNotFound(id))
        : order.LockedBy is { } by ? Result.Fail<decimal>(new OrderLocked(id, by))
        : Result.Ok(order.Total);

    private static Task<Result<Receipt>> ChargeAsync(int id, string card, decimal total) =>
        Task.FromResult(card == "0000"
            ? Result.Fail<Receipt>(new PaymentFailed(id, new CardDeclined("insufficient funds")))
            : Result.Ok(new Receipt(id, total)));

    private static Task<Result<Receipt>> CheckoutAsync(CheckoutRequest request) =>
        Validation.Combine(OrderId(request.OrderId), Card(request.Card), (id, card) => (id, card))
            .ToResult()
            .WithContext("validating the request")
            .BindAsync(r => Load(r.id).WithContext("loading the order").BindAsync(total => ChargeAsync(r.id, r.card, total)));

    private static async Task<Result<Receipt>> OverTheWireAsync(CheckoutRequest request)
    {
        var options = Wire.Web();
        var sent = await CheckoutAsync(request);
        var received = JsonSerializer.Deserialize<Result<Receipt>>(JsonSerializer.Serialize(sent, options), options);
        Assert.Equal(sent, received);
        return received;
    }

    [Fact]
    public async Task A_paid_order_sends_its_receipt() =>
        JsonAssert.Equal("""{"ok":true,"value":{"orderId":1,"total":12.5}}""", await OverTheWireAsync(new CheckoutRequest(1, "1234")));

    [Fact]
    public async Task Every_validation_error_is_sent_together() =>
        JsonAssert.EqualOrdered(
            """
            {"ok":false,"error":{"type":"ValidationFailed<Error>","message":"Validation failed.","context":["validating the request"],
              "errors":[
                {"type":"MessageError","message":"the order id must be positive","context":[]},
                {"type":"InvalidItem","message":"invalid item","context":[],"price":2}]}}
            """,
            await OverTheWireAsync(new CheckoutRequest(0, "12")));

    [Fact]
    public async Task A_locked_order_says_who_holds_it() =>
        JsonAssert.Equal(
            """{"ok":false,"error":{"type":"OrderLocked","message":"order 2 is locked by ann","context":["loading the order"],"id":2,"by":"ann"}}""",
            await OverTheWireAsync(new CheckoutRequest(2, "1234")));

    [Fact]
    public async Task A_declined_card_keeps_the_hierarchy_leaf_inside_the_domain_error()
    {
        var result = await OverTheWireAsync(new CheckoutRequest(3, "0000"));
        JsonAssert.Equal(
            """
            {"ok":false,"error":{"type":"PaymentFailed","message":"payment for order 3 failed","context":[],"orderId":3,
              "reason":{"type":"CardDeclined","message":"card declined: insufficient funds","context":[],"reason":"insufficient funds"}}}
            """,
            result);
        Assert.True(result.Error is PaymentFailed { Reason: CardDeclined { Reason: "insufficient funds" } });
    }
}
