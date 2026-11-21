using Resultful.CodeAnalysis;

namespace Resultful.Assertions.TestDomain;

/// <summary>A closed error hierarchy, as a service's errors are written.</summary>
[ClosedHierarchy]
public abstract partial record OrderError(string Message) : Error(Message);

public sealed record OrderNotFound(int OrderId) : OrderError($"order {OrderId} was not found");

public sealed record OutOfStock(string Sku) : OrderError($"{Sku} is out of stock");

/// <summary>Holds personal data: <see cref="CardHolder"/> must never reach a failure message.</summary>
public sealed record PaymentDeclined(string CardHolder) : OrderError("the payment was declined");

public sealed record Required(string Field) : Error($"{Field} is required");

public sealed record TooMany(string Field, int Max) : Error($"{Field} is more than {Max}");

public sealed record SaveFailed(int OrderId) : Error($"order {OrderId} was not saved");

public sealed record DbTimeout() : Error("the database timed out");

public sealed record Order(int Id, string Sku, int Quantity);

public sealed record OrderRequest(string? Sku, int Quantity, string CardHolder);

[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

[ResultShape(typeof(OrderError), Success = typeof(Order), ManyErrors = true)]
public readonly partial struct CheckOrderResult;

#if NET11_0_OR_GREATER
public sealed record GatewayTimeout() : Error("the payment gateway timed out");

/// <summary>A C# 15 union of errors; the generator adds <see cref="IErrorUnion"/>.</summary>
public partial union CheckoutError(OrderNotFound, PaymentDeclined, GatewayTimeout);

public sealed record Receipt(int OrderId, string Code);

[ResultShape(typeof(CheckoutError), Success = typeof(Receipt))]
public readonly partial struct CheckoutResult;
#endif

/// <summary>The values tests use, so a sentinel can be searched for in every message.</summary>
public static class Sentinels
{
    /// <summary>A card holder's name: personal data that no failure message may contain.</summary>
    public const string CardHolder = "SENTINEL-Jane-Q-Cardholder-4111";
}

/// <summary>A small service that returns results, as code under test does.</summary>
public sealed class OrderService
{
    private readonly Dictionary<string, int> _stock = new(StringComparer.Ordinal) { ["tea"] = 5, ["mug"] = 0 };
    private readonly Dictionary<int, Order> _orders = new() { [1] = new Order(1, "tea", 2) };
    private int _nextId = 2;

    /// <summary>Checks every field and reports every problem.</summary>
    public Validation<OrderRequest> Check(OrderRequest request)
    {
        var sku = string.IsNullOrWhiteSpace(request.Sku) ? Validation.Fail<string>(new Required("sku")) : Validation.Ok(request.Sku!);
        var quantity = request.Quantity > 10 ? Validation.Fail<int>(new TooMany("quantity", 10)) : Validation.Ok(request.Quantity);
        return Validation.Combine(sku, quantity).Map(_ => request);
    }

    /// <summary>Places an order, as a generated shape: the first failure only.</summary>
    public PlaceOrderResult Place(OrderRequest request)
    {
        if (request.CardHolder.Length == 0 || request.CardHolder.StartsWith("DECLINE", StringComparison.Ordinal) || request.CardHolder == Sentinels.CardHolder)
        {
            return new PaymentDeclined(request.CardHolder);
        }

        if (!_stock.TryGetValue(request.Sku ?? string.Empty, out var left) || left < request.Quantity)
        {
            return new OutOfStock(request.Sku ?? string.Empty);
        }

        _stock[request.Sku!] = left - request.Quantity;
        var order = new Order(_nextId++, request.Sku!, request.Quantity);
        _orders[order.Id] = order;
        return order;
    }

    /// <summary>Checks the order every way, as a gathering shape.</summary>
    public CheckOrderResult Audit(OrderRequest request)
    {
        var errors = new List<OrderError>();
        if (!_stock.TryGetValue(request.Sku ?? string.Empty, out var left) || left < request.Quantity)
        {
            errors.Add(new OutOfStock(request.Sku ?? string.Empty));
        }

        if (request.CardHolder.StartsWith("DECLINE", StringComparison.Ordinal) || request.CardHolder == Sentinels.CardHolder)
        {
            errors.Add(new PaymentDeclined(request.CardHolder));
        }

        return errors.Count == 0 ? new Order(0, request.Sku!, request.Quantity) : CheckOrderResult.Fail(errors);
    }

    /// <summary>Validation, then placing, end to end and asynchronously.</summary>
    public async Task<Result<Order>> SubmitAsync(OrderRequest request)
    {
        await Task.Yield();
        return Check(request)
            .ToResult()
            .Bind(valid => Place(valid).AsResult().MapError(e => (Error)e))
            .MapError(e => e.WithContext("submitting an order"));
    }

    /// <summary>Loads an order; a storage failure is wrapped, so the leaf is a cause.</summary>
    public Result<Order, OrderError> Load(int id)
        => _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);

    /// <summary>Saves an order; the database's error is the cause.</summary>
    public Result<Unit> Save(int id)
        => id == 13 ? new SaveFailed(id) { Cause = new DbTimeout() }.WithContext("saving") : Unit.Value;

    /// <summary>Finds an order, or none.</summary>
    public Option<Order> Find(int id)
        => _orders.TryGetValue(id, out var order) ? order : Option<Order>.None;

#if NET11_0_OR_GREATER
    /// <summary>Pays for an order: the error is a C# 15 union.</summary>
    public CheckoutResult Checkout(int orderId, string cardHolder)
    {
        if (!_orders.ContainsKey(orderId))
        {
            return new OrderNotFound(orderId);
        }

        if (cardHolder == "TIMEOUT")
        {
            return new GatewayTimeout();
        }

        return cardHolder.StartsWith("DECLINE", StringComparison.Ordinal) || cardHolder == Sentinels.CardHolder
            ? new PaymentDeclined(cardHolder)
            : new Receipt(orderId, $"R-{orderId}");
    }
#endif
}
