using Resultful.Assertions.TestDomain;

namespace Resultful.Assertions.Tests;

// How a test of a result-returning service reads with Resultful.Assertions: each assertion hands back what comes next.
public class ScenarioTests
{
    private readonly OrderService _service = new();

    [Fact]
    public async Task An_order_is_checked_placed_found_and_saved()
    {
        var request = new OrderRequest("tea", 2, "Ada");

        Assert.Same(request, _service.Check(request).ShouldSucceed("the request is complete"));
        var order = (await _service.SubmitAsync(request)).ShouldSucceed();
        Assert.Equal(("tea", 2), (order.Sku, order.Quantity));

        Assert.Equal(order, _service.Load(order.Id).ShouldSucceed());
        Assert.Equal(order, _service.Find(order.Id).ShouldBeSome());
        Assert.Same(Unit.Value, _service.Save(order.Id).ShouldSucceed());
    }

    [Fact]
    public async Task Each_way_an_order_goes_wrong_is_asserted_by_its_leaf()
    {
        var invalid = _service.Check(new OrderRequest(" ", 11, "Ada"));
        Assert.Equal(2, invalid.ShouldFail().Count);
        Assert.Equal("quantity", invalid.ShouldFail().ShouldFind<TooMany>().Field);

        // The async pipeline stops at the first failure and adds context; the leaf is still found.
        var submitted = await _service.SubmitAsync(new OrderRequest("mug", 1, "Ada"));
        Assert.Equal("mug", submitted.ShouldFail("mugs are sold out").ShouldFind<OutOfStock>().Sku);
        Assert.Equal(["submitting an order"], submitted.ShouldFail().Context);

        var declined = _service.Place(new OrderRequest("tea", 1, Sentinels.CardHolder));
        var payment = declined.ShouldFail().ShouldFind<PaymentDeclined>();
        Assert.Equal(Sentinels.CardHolder, payment.CardHolder);

        var missing = _service.Load(99).ShouldFail().ShouldFind<OrderNotFound>();
        Assert.Equal(99, missing.OrderId);
        _service.Find(99).ShouldBeNone();

        // The database's error is the cause, under the error the service returned.
        Assert.IsType<DbTimeout>(_service.Save(13).ShouldFail().ShouldFind<DbTimeout>());
        var audit = _service.Audit(new OrderRequest("mug", 1, "DECLINE"));
        Assert.Equal(2, audit.ShouldFail().Count);
    }

    [Fact]
    public void A_wrong_expectation_explains_itself_without_personal_data()
    {
        var declined = _service.Place(new OrderRequest("tea", 1, Sentinels.CardHolder));
        var message = Assert.Throws<ResultAssertionException>(() => declined.ShouldSucceed("the card is valid")).Message;
        Assert.Equal(
            """
            Expected PlaceOrderResult to succeed because the card is valid, but it failed with:
              PaymentDeclined: the payment was declined
            """,
            message);
        Assert.DoesNotContain(Sentinels.CardHolder, message, StringComparison.Ordinal);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Checkout_returns_a_union_shape_and_each_case_is_asserted()
    {
        Assert.Equal("R-1", _service.Checkout(1, "Ada").ShouldSucceed().Code);
        Assert.Equal(5, _service.Checkout(5, "Ada").ShouldFail().ShouldFind<OrderNotFound>().OrderId);
        Assert.IsType<GatewayTimeout>(_service.Checkout(1, "TIMEOUT").ShouldFail().Error);
        var message = Assert.Throws<ResultAssertionException>(() => _service.Checkout(1, Sentinels.CardHolder).ShouldFail().ShouldFind<GatewayTimeout>()).Message;
        Assert.Equal("Expected the error to be or contain GatewayTimeout, but it is:\n  PaymentDeclined: the payment was declined", message);
    }
#endif
}
