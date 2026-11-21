using System.Runtime.CompilerServices;
using Resultful.AwaitProbe.ShortCircuit;

namespace Resultful.AwaitProbe.Tests;

/// <summary>Correctness of `await` on a result as a short-circuit: the findings the exploration note records.</summary>
public sealed class ShortCircuitTests(ITestOutputHelper output)
{
    public static readonly TheoryData<string, OrderDto, string[]> Steps = new()
    {
        { "success", new("sku-1", 2, "4000"), ["parse", "reserve", "charge"] },
        { "first step fails", new(null, 2, "4000"), ["parse"] },
        { "middle step fails", new("sku-1", 99, "4000"), ["parse", "reserve"] },
        { "last step fails", new("sku-1", 2, "5000"), ["parse", "reserve", "charge"] },
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
    public void Every_style_returns_the_first_failure_and_runs_no_later_step(string name, OrderDto dto, string[] log)
    {
        var styles = new (string Style, Func<Shop, OrderDto, Result<Receipt>> Run)[]
        {
            ("await", (s, d) => s.PlaceOrderAwait(d)),
            ("await, unwinding", (s, d) => s.PlaceOrderUnwinding(d)),
            ("Bind", (s, d) => s.PlaceOrderBind(d)),
            ("early return", (s, d) => s.PlaceOrderEarlyReturn(d)),
        };
        foreach (var (style, run) in styles)
        {
            var shop = new Shop();
            Assert.Equal((style, Expected[name], string.Join(",", log)), (style, run(shop, dto), string.Join(",", shop.Log)));
        }
    }

    [Fact]
    public void An_exception_in_a_step_propagates_from_the_call_unwrapped()
    {
        var dto = new OrderDto("boom", 1, "4000");
        Assert.Equal("the payment gateway is down", Assert.Throws<InvalidOperationException>(() => new Shop().PlaceOrderAwait(dto)).Message);
        Assert.Equal("the payment gateway is down", Assert.Throws<InvalidOperationException>(() => new Shop().PlaceOrderUnwinding(dto)).Message);
    }

    [Fact]
    public void Awaiting_the_zero_value_throws_as_reading_it_does()
    {
        Assert.Contains("default", Assert.Throws<InvalidOperationException>(() => AwaitZero()).Message, StringComparison.Ordinal);
        Assert.Contains("default", Assert.Throws<InvalidOperationException>(() => AwaitZeroUnwinding()).Message, StringComparison.Ordinal);

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        static async Result<int> AwaitZero() => await default(Result<int>);

        [AsyncMethodBuilder(typeof(UnwindingResultBuilder<>))]
        static async Result<int> AwaitZeroUnwinding() => await default(Result<int>);
    }

    [Fact]
    public void A_sync_result_method_cannot_await_real_work_but_can_await_a_completed_task()
    {
        Assert.Equal(Sync.RealAwait, Assert.Throws<NotSupportedException>(() => AwaitDelay()).Message);
        Assert.Equal(3.Ok(), AwaitCompleted());

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        static async Result<int> AwaitDelay()
        {
            await Task.Delay(1);
            return 1;
        }

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        static async Result<int> AwaitCompleted() => await Task.FromResult(3);
    }

    [Fact]
    public void Abandoning_skips_finally_and_using_after_the_failed_await()
    {
        var shop = new Shop();
        Assert.Equal(new InvalidOrder("sku"), Run(shop, new(null, 1, "4000")));
        Assert.Equal(["open", "parse"], shop.Log);

        // The same method on success runs both.
        var ok = new Shop();
        Assert.True(Run(ok, new("sku", 1, "4000")).IsSuccess);
        Assert.Equal(["open", "parse", "dispose lease", "finally"], ok.Log);

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        static async Result<Order> Run(Shop shop, OrderDto dto)
        {
            try
            {
                shop.Log.Add("open");
                using var lease = shop.Open("lease");
                return await shop.ParseOrder(dto);
            }
            finally
            {
                shop.Log.Add("finally");
            }
        }
    }

    [Fact]
    public void Unwinding_runs_finally_and_using_in_order_and_still_returns_the_failure()
    {
        var shop = new Shop();
        Assert.Equal(new InvalidOrder("sku"), Run(shop, new(null, 1, "4000")));
        Assert.Equal(["open", "parse", "dispose lease", "finally"], shop.Log);

        [AsyncMethodBuilder(typeof(UnwindingResultBuilder<>))]
        static async Result<Order> Run(Shop shop, OrderDto dto)
        {
            try
            {
                shop.Log.Add("open");
                using var lease = shop.Open("lease");
                return await shop.ParseOrder(dto);
            }
            finally
            {
                shop.Log.Add("finally");
            }
        }
    }

    [Fact]
    public void Unwinding_is_caught_by_a_catch_all_in_the_method_unless_a_filter_excludes_it()
    {
        var shop = new Shop();
        // The catch-all swallows the short-circuit: the method goes on and returns a success it shouldn't.
        Assert.Equal(new Order("fallback", 0, ""), CatchAll(shop, new(null, 1, "4000")));
        Assert.Equal(["parse", "caught AwaitedFailureException"], shop.Log);

        var filtered = new Shop();
        Assert.Equal(new InvalidOrder("sku"), Filtered(filtered, new(null, 1, "4000")));
        Assert.Equal(["parse"], filtered.Log);

        [AsyncMethodBuilder(typeof(UnwindingResultBuilder<>))]
        static async Result<Order> CatchAll(Shop shop, OrderDto dto)
        {
            try
            {
                return await shop.ParseOrder(dto);
            }
            catch (Exception e)
            {
                shop.Log.Add($"caught {e.GetType().Name}");
                return new Order("fallback", 0, "");
            }
        }

        [AsyncMethodBuilder(typeof(UnwindingResultBuilder<>))]
        static async Result<Order> Filtered(Shop shop, OrderDto dto)
        {
            try
            {
                return await shop.ParseOrder(dto);
            }
            catch (Exception e) when (e is not AwaitedFailureException)
            {
                shop.Log.Add($"caught {e.GetType().Name}");
                return new Order("fallback", 0, "");
            }
        }
    }

    [Fact]
    public void Loops_stop_at_the_first_failed_item()
    {
        Assert.Equal(6.Ok(), Sum([1, 2, 3]));
        Assert.Equal(new InvalidOrder("item 2"), Sum([1, -2, 3, -4]));

        static Result<int> Check(int x, int i) => x >= 0 ? x : new InvalidOrder($"item {i}");

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        static async Result<int> Sum(int[] items)
        {
            var total = 0;
            for (var i = 0; i < items.Length; i++) total += await Check(items[i], i + 1);
            return total;
        }
    }

    [Fact]
    public void A_typed_result_with_an_error_derived_from_Error_can_be_awaited_in_a_Result_T_method()
    {
        Assert.Equal(new OutOfStock("a"), Run(0));
        Assert.Equal(2.Ok(), Run(1));

        static Result<int, OutOfStock> Typed(int n) => n > 0 ? n : new OutOfStock("a");

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        static async Result<int> Run(int n) => await Typed(n) * 2;
    }

    [Fact]
    public void Typed_errors_go_through_ValueTask_and_complete_synchronously_without_real_awaits()
    {
        var shop = new Shop();
        Assert.Equal(
            [15.Ok<int, ShopError>(), new NotFound("missing"), new Closed()],
            [Completed(shop.Quote("sku", 3)), Completed(shop.Quote("missing", 3)), Completed(shop.Quote("closed", 3))]);

        // CA2012 flags reading a ValueTask's Result unless it has completed: a caller has to check, or await.
        static Result<int, ShopError> Completed(ValueTask<Result<int, ShopError>> quote) =>
            quote.IsCompletedSuccessfully ? quote.Result : throw new InvalidOperationException("not completed synchronously");
    }

    [Fact]
    public async Task An_error_type_the_method_cannot_hold_fails_at_run_time_only_on_the_failure_path()
    {
        Assert.Equal(1.Ok<int, ShopError>(), await Mismatched(true));
        Assert.Equal(FailureFactory.Mismatch, (await Assert.ThrowsAsync<InvalidOperationException>(async () => await Mismatched(false))).Message);

        [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
        static async ValueTask<Result<int, ShopError>> Mismatched(bool ok) => await (ok ? 1.Ok() : Error.From("untyped").Fail<int>());
    }

    [Fact]
    public void Values_after_await_are_the_success_values_and_never_null()
    {
        var order = Get(new("sku", 1, "4000")).Value;
        Assert.Equal(("SKU", 4), (order.Sku, order.Card.Length));

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        async Result<Order> Get(OrderDto dto)
        {
            var parsed = await new Shop().ParseOrder(dto);
            // No `!` and no warning: GetResult returns T, so nullable flow sees Order, not Order?.
            return parsed with { Sku = parsed.Sku.ToUpperInvariant() };
        }
    }

    [Fact]
    public async Task An_AsyncLocal_set_in_a_sync_result_method_leaks_to_the_caller_unlike_an_async_Task()
    {
        var local = new AsyncLocal<string?>();
        _ = SetInResult(local);
        Assert.Equal("set", local.Value);

        var other = new AsyncLocal<string?>();
        await SetInTask(other);
        Assert.Null(other.Value);

        [AsyncMethodBuilder(typeof(ResultBuilder<>))]
        static async Result<int> SetInResult(AsyncLocal<string?> l)
        {
            l.Value = "set";
            return await 1.Ok();
        }

        static async Task SetInTask(AsyncLocal<string?> l)
        {
            l.Value = "set";
            await Task.CompletedTask;
        }
    }

    [Fact]
    public void Stack_traces_show_the_method_twice_and_no_builder_frames()
    {
        var bad = new OrderDto("boom", 1, "4000");
        var awaitTrace = Assert.Throws<InvalidOperationException>(() => new Shop().PlaceOrderAwait(bad)).StackTrace!;
        var unwindingTrace = Assert.Throws<InvalidOperationException>(() => new Shop().PlaceOrderUnwinding(bad)).StackTrace!;
        var bindTrace = Assert.Throws<InvalidOperationException>(() => new Shop().PlaceOrderBind(bad)).StackTrace!;
        output.WriteLine($"await:\n{awaitTrace}\n\nunwinding:\n{unwindingTrace}\n\nBind:\n{bindTrace}");

        // The state machine's MoveNext (with a line number) and the kickoff method, both shown under the method's name.
        Assert.Equal(["Shop.Charge(Order order, Int32 amount)", "Shop.PlaceOrderAwait(OrderDto dto)", "Shop.PlaceOrderAwait(OrderDto dto)"], Frames(awaitTrace));
        Assert.Equal(["Shop.Charge(Order order, Int32 amount)", "Shop.PlaceOrderUnwinding(OrderDto dto)", "Shop.PlaceOrderUnwinding(OrderDto dto)"], Frames(unwindingTrace));
        // The Bind chain shows a frame per lambda (plus Bind and Match where the JIT doesn't inline them: 8 frames unoptimised, 4 optimised).
        Assert.Equal(2, Frames(bindTrace).Count(f => f.Contains("<PlaceOrderBind>b__", StringComparison.Ordinal)));

        static string[] Frames(string trace) =>
        [
            .. trace.Split('\n')
                .Select(line => line.Trim())
                .TakeWhile(line => !line.Contains(nameof(ShortCircuitTests), StringComparison.Ordinal))
                .Select(line => line["at ".Length..].Split(" in ")[0].Replace("Resultful.AwaitProbe.Tests.", "", StringComparison.Ordinal)),
        ];
    }
}
