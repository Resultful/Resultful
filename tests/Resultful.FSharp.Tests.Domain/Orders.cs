using Resultful.CodeAnalysis;

namespace Resultful.FSharp.Tests.Domain;

public sealed record Order(int Id, string Customer, decimal Total);

[ClosedHierarchy]
public abstract record OrderError : Error
{
    private protected OrderError(string message) : base(message) { }
}

public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");

// Card holds personal data: Error.ToString never prints it.
public sealed record PaymentDeclined(int Id, string Card) : OrderError($"payment for order {Id} was declined");

// The checks' messages, joined, when an order is rejected before it is charged.
public sealed record OrderRejected(string Reasons) : OrderError($"the order was rejected: {Reasons}");

public sealed record Required(string Field) : Error($"{Field} is required");

public sealed record OutOfRange(string Field, decimal Value) : Error($"{Field} is out of range");

/// <summary>A C# service as F# code meets it: Resultful results, a validation and an option.</summary>
public sealed class OrderService
{
    private readonly Dictionary<int, Order> _orders = new()
    {
        [1] = new Order(1, "ada", 30m),
        [2] = new Order(2, "grace", 120m),
    };

    public Result<Order, OrderError> Find(int id)
        => _orders.TryGetValue(id, out var order) ? order.Ok<Order, OrderError>() : new OrderNotFound(id);

    public Option<Order> TryFind(int id)
        => _orders.TryGetValue(id, out var order) ? order.Some() : Option<Order>.None;

    public Validation<Order> Check(string customer, decimal total)
        => Validation.Combine(
            string.IsNullOrEmpty(customer) ? Validation.Fail<string>(new Required("customer")) : Validation.Ok(customer),
            total is <= 0m or > 1000m ? Validation.Fail<decimal>(new OutOfRange("total", total)) : Validation.Ok(total),
            (c, t) => new Order(_orders.Count + 1, c, t));

    public Result<Order, OrderError> Charge(Order order, string card)
        => order.Total > 100m ? new PaymentDeclined(order.Id, card) : order.Ok<Order, OrderError>();

    public Result<Unit, OrderError> Save(Order order)
    {
        _orders[order.Id] = order;
        return Result.Ok<OrderError>();
    }

    public Task<Result<Order, OrderError>> FindAsync(int id) => Task.FromResult(Find(id));
}
