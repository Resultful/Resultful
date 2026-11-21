using HotChocolate;
using Microsoft.Extensions.DependencyInjection;

namespace Resultful.HotChocolate.Tests;

public sealed record QuantityOutOfRange(int Min, int Max) : Error($"The quantity must be from {Min} to {Max}.");

/// <summary>The order store: stock per SKU, and the orders placed so far.</summary>
public sealed class Inventory
{
    private readonly Dictionary<string, int> _stock = new(StringComparer.Ordinal) { ["BOOK"] = 3, ["PEN"] = 0 };
    private readonly List<Order> _orders = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<Order> Orders
    {
        get
        {
            lock (_gate) return [.. _orders];
        }
    }

    public async Task<Result<Order, OrderError>> ReserveAsync(int customerId, string sku, int quantity)
    {
        await Task.Yield();
        if (customerId == 13) return new CustomerBlocked(customerId);
        lock (_gate)
        {
            if (!_stock.TryGetValue(sku, out var left) || left < quantity) return new OutOfStock(sku);
            _stock[sku] = left - quantity;
            var order = new Order(_orders.Count + 1, sku, quantity);
            _orders.Add(order);
            return order;
        }
    }
}

public sealed class ShopQuery
{
    public IReadOnlyList<Order> Orders([Service] Inventory inventory) => inventory.Orders;
}

public sealed class ShopMutation
{
    // Validation first (every problem at once), then the domain step, which may fail with an OrderError.
    public async Task<Result<Order>> PlaceOrder(int customerId, string sku, int quantity, [Service] Inventory inventory)
    {
        var problems = new List<Error>();
        if (string.IsNullOrWhiteSpace(sku)) problems.Add(new Required("sku"));
        if (quantity is < 1 or > 10) problems.Add(new QuantityOutOfRange(1, 10));
        if (problems.Count > 0) return Result.Fail<Order>(new ValidationFailed<Error>(problems));

        var reserved = await inventory.ReserveAsync(customerId, sku, quantity);
        return reserved.MapError(error => (Error)error.WithContext($"placing an order for customer {customerId}"));
    }
}

// A client places orders through the mutation, as a front end would: one operation with variables and fragments.
public class ScenarioTests
{
    private const string PlaceOrder = """
        mutation PlaceOrder($input: PlaceOrderInput!) {
          placeOrder(input: $input) {
            order { id sku quantity }
            errors {
              __typename
              ... on Error { message }
              ... on ValidationError { fields { field messages } }
              ... on OutOfStock { sku }
              ... on CustomerBlocked { customerId }
            }
          }
        }
        """;

    [Fact]
    public async Task An_order_goes_from_validation_errors_to_domain_errors_to_placed()
    {
        var executor = await Graph.BuildAsync(
            b => b.AddQueryType<ShopQuery>().AddMutationType<ShopMutation>().AddMutationConventions().Services.AddSingleton<Inventory>(),
            o =>
            {
                o.AddErrorCases<Error>(OrderError.Cases).ExposeFieldsOf<OutOfStock>().ExposeFieldsOf<CustomerBlocked>();
                o.ValidationKey = e => e switch { Required r => r.Field, QuantityOutOfRange => "quantity", _ => e.GetType().Name };
            });

        Assert.Equal(
            """{"data":{"placeOrder":{"order":null,"errors":[{"__typename":"ValidationError","message":"Validation failed.","fields":[{"field":"sku","messages":["sku is required."]},{"field":"quantity","messages":["The quantity must be from 1 to 10."]}]}]}}}""",
            await Place(executor, customerId: 1, sku: " ", quantity: 0));
        Assert.Equal(
            """{"data":{"placeOrder":{"order":null,"errors":[{"__typename":"OutOfStock","message":"PEN is out of stock.","sku":"PEN"}]}}}""",
            await Place(executor, customerId: 1, sku: "PEN", quantity: 1));
        Assert.Equal(
            """{"data":{"placeOrder":{"order":null,"errors":[{"__typename":"CustomerBlocked","message":"The customer can't order.","customerId":13}]}}}""",
            await Place(executor, customerId: 13, sku: "BOOK", quantity: 1));
        Assert.Equal(
            """{"data":{"placeOrder":{"order":{"id":1,"sku":"BOOK","quantity":2},"errors":null}}}""",
            await Place(executor, customerId: 1, sku: "BOOK", quantity: 2));
        Assert.Equal(
            """{"data":{"placeOrder":{"order":null,"errors":[{"__typename":"OutOfStock","message":"BOOK is out of stock.","sku":"BOOK"}]}}}""",
            await Place(executor, customerId: 2, sku: "BOOK", quantity: 2));
        Assert.Equal(
            """{"data":{"orders":[{"id":1,"sku":"BOOK","quantity":2}]}}""",
            await Graph.RunJsonAsync(executor, "query Orders { orders { id sku quantity } }"));
    }

    [Fact]
    public async Task The_payload_declares_validation_and_every_order_error()
    {
        var executor = await Graph.BuildAsync(
            b => b.AddQueryType<ShopQuery>().AddMutationType<ShopMutation>().AddMutationConventions().Services.AddSingleton<Inventory>(),
            o => o.AddErrorCases<Error>(OrderError.Cases));

        var sdl = Graph.Sdl(executor);

        Assert.Contains("type PlaceOrderPayload {\n  order: Order\n  errors: [PlaceOrderError!]\n}", sdl, StringComparison.Ordinal);
        Assert.Contains("union PlaceOrderError = CustomerBlocked | OutOfStock | ValidationError", sdl, StringComparison.Ordinal);
    }

    private static Task<string> Place(global::HotChocolate.Execution.IRequestExecutor executor, int customerId, string sku, int quantity) =>
        Graph.RunJsonAsync(
            executor,
            PlaceOrder,
            variables: new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["customerId"] = customerId, ["sku"] = sku, ["quantity"] = quantity },
            });
}
