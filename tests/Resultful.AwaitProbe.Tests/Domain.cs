using System.Runtime.CompilerServices;
using Resultful.AwaitProbe.ShortCircuit;

namespace Resultful.AwaitProbe.Tests;

public sealed record OrderDto(string? Sku, int Quantity, string? Card);
public sealed record Order(string Sku, int Quantity, string Card);
public sealed record Stock(string Sku, int Reserved);
public sealed record Payment(string Id, int Amount);
public sealed record Receipt(string Sku, int Quantity, string PaymentId);

public sealed record InvalidOrder(string Field) : Error($"{Field} is invalid");
public sealed record OutOfStock(string Sku) : Error("the item is out of stock");
// A leaf property that must never reach an exception message: the card number.
public sealed record CardDeclined(string Card) : Error("the card was declined");

// A typed error hierarchy for Result<T, TError>.
public abstract record ShopError(string Code);
public sealed record NotFound(string Sku) : ShopError("not-found");
public sealed record Closed() : ShopError("closed");

/// <summary>Records each step it runs, and the resources it opens and closes.</summary>
public sealed class Shop
{
    public const string Sentinel = "5111-SENTINEL-1111";

    public List<string> Log { get; } = [];

    public int Stock { get; set; } = 10;

    public Result<Order> ParseOrder(OrderDto dto)
    {
        Log.Add("parse");
        if (dto.Sku is null) return new InvalidOrder("sku");
        if (dto.Quantity <= 0) return new InvalidOrder("quantity");
        if (dto.Card is null) return new InvalidOrder("card");
        return new Order(dto.Sku, dto.Quantity, dto.Card);
    }

    public Result<Stock> Reserve(Order order)
    {
        Log.Add("reserve");
        return order.Quantity <= Stock ? new Stock(order.Sku, order.Quantity) : new OutOfStock(order.Sku);
    }

    public Result<Payment> Charge(Order order, int amount)
    {
        Log.Add("charge");
        if (order.Sku == "boom") throw new InvalidOperationException("the payment gateway is down");
        return order.Card.StartsWith('4') ? new Payment("pay-1", amount) : new CardDeclined(order.Card);
    }

    public async Task<Result<Stock>> ReserveAsync(Order order)
    {
        await Task.Yield();
        Log.Add("reserve-async");
        return Reserve(order);
    }

    public async Task<Result<Payment>> ChargeAsync(Order order, int amount)
    {
        await Task.Delay(1);
        Log.Add("charge-async");
        return Charge(order, amount);
    }

    public static int Total(Order order) => order.Quantity * 5;

    public Lease Open(string name) => new(this, name);

    public sealed class Lease(Shop shop, string name) : IDisposable
    {
        public void Dispose() => shop.Log.Add($"dispose {name}");
    }

    // The same handler in each style. The await forms are what decision 22 item 6 asks about.

    [AsyncMethodBuilder(typeof(ResultBuilder<>))]
    public async Result<Receipt> PlaceOrderAwait(OrderDto dto)
    {
        var order = await ParseOrder(dto);
        var stock = await Reserve(order);
        var payment = await Charge(order, Total(order));
        return new Receipt(stock.Sku, stock.Reserved, payment.Id);
    }

    [AsyncMethodBuilder(typeof(UnwindingResultBuilder<>))]
    public async Result<Receipt> PlaceOrderUnwinding(OrderDto dto)
    {
        var order = await ParseOrder(dto);
        var stock = await Reserve(order);
        var payment = await Charge(order, Total(order));
        return new Receipt(stock.Sku, stock.Reserved, payment.Id);
    }

    public Result<Receipt> PlaceOrderBind(OrderDto dto) =>
        ParseOrder(dto).Bind(order => Reserve(order).Bind(stock => Charge(order, Total(order)).Map(payment => new Receipt(stock.Sku, stock.Reserved, payment.Id))));

    public Result<Receipt> PlaceOrderEarlyReturn(OrderDto dto)
    {
        var parsed = ParseOrder(dto);
        if (!parsed.TryGetValue(out var order)) return parsed.Error;
        var reserved = Reserve(order);
        if (!reserved.TryGetValue(out var stock)) return reserved.Error;
        var charged = Charge(order, Total(order));
        if (!charged.TryGetValue(out var payment)) return charged.Error;
        return new Receipt(stock.Sku, stock.Reserved, payment.Id);
    }

    // Real awaits mixed with result awaits.

    [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
    public async ValueTask<Result<Receipt>> PlaceOrderAsync(OrderDto dto)
    {
        var order = await ParseOrder(dto);
        using var lease = Open("connection");
        await Task.Delay(1);
        var stock = await await ReserveAsync(order);
        var payment = await ChargeAsync(order, Total(order)).ShortCircuit();
        return new Receipt(stock.Sku, stock.Reserved, payment.Id);
    }

    [AsyncMethodBuilder(typeof(UnwindingAsyncResultBuilder<>))]
    public async ValueTask<Result<Receipt>> PlaceOrderAsyncUnwinding(OrderDto dto)
    {
        var order = await ParseOrder(dto);
        using var lease = Open("connection");
        await Task.Delay(1);
        var stock = await await ReserveAsync(order);
        var payment = await ChargeAsync(order, Total(order)).ShortCircuit();
        return new Receipt(stock.Sku, stock.Reserved, payment.Id);
    }

    // Typed errors: Result<T, TError> has two type parameters, so it can't be a method-level builder's return type
    // (CS1983). ValueTask<Result<T, TError>> has one, so the async builder serves it, completing synchronously here.

    public Result<string, ShopError> Find(string sku) => sku == "missing" ? new NotFound(sku) : sku;

    public Result<int, ShopError> Price(string sku) => sku == "closed" ? new Closed() : 5;

    [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
    public async ValueTask<Result<int, ShopError>> Quote(string sku, int quantity)
    {
        var found = await Find(sku);
        var price = await Price(found);
        return price * quantity;
    }
}
