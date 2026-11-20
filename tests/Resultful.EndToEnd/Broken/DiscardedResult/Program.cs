using Resultful;
using Resultful.Shop.Domain;

namespace Resultful.EndToEnd.Broken;

public sealed record Basket(int OrderId);

public sealed class Baskets
{
    public IEnumerable<Basket> Abandoned() => [new Basket(1)];
}

public sealed class Orders
{
    public Result<Unit, OrderError> Cancel(int id) => new OrderNotFound(id);
}

// Use case 7 before the fix, as the docs show it: the result of Cancel is thrown away.
public sealed class CancelJob(Baskets baskets, Orders orders)
{
    private readonly Baskets _baskets = baskets;
    private readonly Orders _orders = orders;

    #region snippet: uc07-broken
    public void CancelAbandonedBaskets()
    {
        foreach (var basket in _baskets.Abandoned())
            _orders.Cancel(basket.OrderId);             // returns Result<Unit, OrderError>
    }
    #endregion
}
