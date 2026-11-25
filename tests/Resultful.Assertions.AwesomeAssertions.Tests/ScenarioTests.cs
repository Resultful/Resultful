using Resultful.Assertions.TestDomain;
#if RESULTFUL_FLUENTASSERTIONS
using FluentAssertions;
using FluentAssertions.Execution;
#else
using AwesomeAssertions;
using AwesomeAssertions.Execution;
#endif

namespace Resultful.Assertions.AdapterTests;

// How a test of a result-returning service reads with the adapter: validation, a generated shape, an async pipeline,
// errors with causes and context, and (net11.0) a union, asserted fluently end to end.
public class ScenarioTests
{
    private readonly OrderService _service = new();

    [Fact]
    public async Task An_order_goes_from_request_to_saved()
    {
        var request = new OrderRequest("tea", 2, "Ada");

        _service.Check(request).Should().Succeed("the request is complete").Which.Should().BeSameAs(request);
        var order = (await _service.SubmitAsync(request)).Should().Succeed().Which;
        order.Should().BeEquivalentTo(new { Sku = "tea", Quantity = 2 });

        _service.Load(order.Id).Should().Succeed().Which.Should().Be(order);
        _service.Find(order.Id).Should().BeSome().Which.Id.Should().Be(order.Id);
        _service.Save(order.Id).Should().Succeed();
    }

    [Fact]
    public async Task Every_way_it_goes_wrong_is_asserted_by_leaf_message_context_and_cause()
    {
        _service.Check(new OrderRequest(null, 11, "Ada")).Should().Fail().Which.Should().HaveCount(2);
        _service.Check(new OrderRequest(null, 1, "Ada")).Should().FailWith<Required>().WithMessage("sku is required");

        (await _service.SubmitAsync(new OrderRequest("mug", 1, "Ada"))).Should()
            .FailWith<OutOfStock>("mugs are sold out")
            .WithMessage("mug is * stock")
            .WithContext("submitting an order")
            .Which.Sku.Should().Be("mug");

        _service.Place(new OrderRequest("tea", 1, Sentinels.CardHolder)).Should()
            .FailWith<PaymentDeclined>()
            .Which.CardHolder.Should().Be(Sentinels.CardHolder);

        _service.Load(99).Should().Fail().WithError<OrderNotFound>().Which.OrderId.Should().Be(99);
        _service.Find(99).Should().BeNone();
        _service.Save(13).Should().Fail().WithContext("saving").WithCause<DbTimeout>().WithMessage("the database timed out");
        _service.Audit(new OrderRequest("mug", 1, "DECLINE")).Should().FailWith<PaymentDeclined>();
    }

    [Fact]
    public void A_scope_reports_every_broken_expectation_without_personal_data()
    {
        // Named as each assertion's default subject, so the message is the same whether or not the library finds this line.
        var result = _service.Place(new OrderRequest("tea", 1, Sentinels.CardHolder));
        var validation = _service.Audit(new OrderRequest("tea", 1, Sentinels.CardHolder));
        var exception = Record.Exception(() =>
        {
            using var scope = new AssertionScope();
            result.Should().Succeed("the card is valid");
            validation.Should().FailWith<OutOfStock>();
        });

        Assert.NotNull(exception);
        Assert.Equal(
            "Expected result to succeed because the card is valid, but it failed with \"PaymentDeclined: the payment was declined\"." + Environment.NewLine
            + "Expected validation to fail with OutOfStock, but it failed with {\"PaymentDeclined: the payment was declined\"}." + Environment.NewLine,
            exception.Message);
        Assert.DoesNotContain(Sentinels.CardHolder, exception.Message, StringComparison.Ordinal);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Checkout_over_a_union_is_asserted_case_by_case()
    {
        _service.Checkout(1, "Ada").Should().Succeed().Which.Code.Should().Be("R-1");
        _service.Checkout(5, "Ada").Should().FailWith<OrderNotFound>().WithMessage("order 5 *");
        _service.Checkout(1, "TIMEOUT").Should().Fail().WithError<GatewayTimeout>();
    }
#endif
}
