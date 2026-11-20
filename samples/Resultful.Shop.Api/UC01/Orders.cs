using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

// Seeded data: 1 is free, 2 is locked by alice, 42 is locked by alice (use case 8).
internal sealed class OrderStore
{
    private readonly Dictionary<int, Order> _orders = new()
    {
        [1] = new Order(1, "teapot", 25m, "4242"),
        [2] = new Order(2, "kettle", 40m, "4242", LockedBy: "alice"),
        [42] = new Order(42, "mugs", 12m, "4242", LockedBy: "alice"),
    };

    public Order? Find(int id) => _orders.GetValueOrDefault(id);
}

internal sealed partial class Orders
{
    private readonly OrderStore _store = new();

    #region snippet: uc01-get
    public Result<Order, OrderError> Get(int id)
    {
        var order = _store.Find(id);
        if (order is null) return new OrderNotFound(id);
        if (order.LockedBy is { } who) return new OrderLocked(id, who);
        return order;
    }
    #endregion
}

internal sealed class ShapedOrders
{
    private readonly OrderStore _store = new();

    // The same body: Order and each leaf convert implicitly into the shape.
    public GetOrderResult Get(int id)
    {
        var order = _store.Find(id);
        if (order is null) return new OrderNotFound(id);
        if (order.LockedBy is { } who) return new OrderLocked(id, who);
        return order;
    }
}
