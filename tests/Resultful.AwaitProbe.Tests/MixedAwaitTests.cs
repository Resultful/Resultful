using System.Runtime.CompilerServices;
using Resultful.AwaitProbe.ShortCircuit;

namespace Resultful.AwaitProbe.Tests;

/// <summary>The ValueTask builders, mixing real awaits with result awaits, and results awaited outside any builder.</summary>
public sealed class MixedAwaitTests
{
    public static readonly TheoryData<OrderDto, string, string[]> Steps = new()
    {
        { new("sku-1", 2, "4000"), "success", ["parse", "reserve-async", "reserve", "charge-async", "charge", "dispose connection"] },
        // Known before any real await.
        { new(null, 2, "4000"), "first step fails", ["parse"] },
        // Known after a real await, awaited as `await await task`: the result is in hand when the builder sees it.
        { new("sku-1", 99, "4000"), "middle step fails", ["parse", "reserve-async", "reserve"] },
        // Pending when awaited through ShortCircuit(): the failure arrives in GetResult, so it unwinds even when abandoning.
        { new("sku-1", 2, "5000"), "last step fails", ["parse", "reserve-async", "reserve", "charge-async", "charge", "dispose connection"] },
    };

    static readonly Dictionary<string, Result<Receipt>> Expected = new()
    {
        ["success"] = new Receipt("sku-1", 2, "pay-1"),
        ["first step fails"] = new InvalidOrder("sku"),
        ["middle step fails"] = new OutOfStock("sku-1"),
        ["last step fails"] = new CardDeclined("5000"),
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task Abandoning_async_returns_the_first_failure_but_disposes_only_when_the_failure_arrived_after_a_wait(OrderDto dto, string name, string[] log)
    {
        var shop = new Shop();
        Assert.Equal(Expected[name], await shop.PlaceOrderAsync(dto));
        Assert.Equal(log, shop.Log);
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task Unwinding_async_returns_the_first_failure_and_always_disposes(OrderDto dto, string name, string[] log)
    {
        var shop = new Shop();
        Assert.Equal(Expected[name], await shop.PlaceOrderAsyncUnwinding(dto));
        var disposed = name == "first step fails" ? log : [.. log.Where(l => l != "dispose connection"), "dispose connection"];
        Assert.Equal(disposed, shop.Log);
    }

    [Fact]
    public async Task An_exception_after_a_real_await_faults_the_ValueTask()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new Shop().PlaceOrderAsync(new("boom", 1, "4000")));
        Assert.Equal("the payment gateway is down", error.Message);
    }

    [Fact]
    public async Task A_result_awaited_in_a_plain_async_Task_faults_it_instead_of_hanging()
    {
        var shop = new Shop();
        var task = Plain(shop, new("sku-1", 2, "5000"));
        var error = await Assert.ThrowsAsync<AwaitedFailureException>(() => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(new CardDeclined("5000"), error.Error);
        // The message never holds the error's contents (personal-data rule).
        Assert.Equal((AwaitedFailureException.Text, false), (error.Message, error.ToString().Contains("5000", StringComparison.Ordinal)));

        var ok = await Plain(new Shop(), new("sku-1", 2, "4000"));
        Assert.Equal("pay-1", ok.Id);

        static async Task<Payment> Plain(Shop shop, OrderDto dto)
        {
            var order = await shop.ParseOrder(dto);
            return await shop.Charge(order, 10);
        }
    }

    [Fact]
    public async Task The_personal_data_sentinel_never_reaches_the_exception_text()
    {
        var error = await Assert.ThrowsAsync<AwaitedFailureException>(() => Plain());
        Assert.DoesNotContain(Shop.Sentinel, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(new CardDeclined(Shop.Sentinel), error.Error);

        static async Task<Payment> Plain() => await new Shop().Charge(new Order("sku", 1, Shop.Sentinel), 1);
    }

    [Fact]
    public async Task A_short_circuit_faulting_a_plain_task_becomes_a_failure_again_in_a_builder_method()
    {
        Assert.Equal(new InvalidOrder("sku"), await Outer());

        static async Task<Order> Plain() => await new Shop().ParseOrder(new(null, 1, "4000"));

        [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
        static async ValueTask<Result<Order>> Outer() => await Plain();
    }

    [Fact]
    public async Task Typed_errors_mix_with_real_awaits()
    {
        Assert.Equal(10.Ok<int, ShopError>(), await Quote("sku"));
        Assert.Equal(new Closed(), await Quote("closed"));

        [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
        static async ValueTask<Result<int, ShopError>> Quote(string sku)
        {
            var shop = new Shop();
            var found = await shop.Find(sku);
            await Task.Yield();
            return await shop.Price(found) * 2;
        }
    }
}
