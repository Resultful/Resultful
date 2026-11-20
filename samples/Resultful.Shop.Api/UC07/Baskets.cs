using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

internal sealed record Basket(int OrderId);

internal sealed partial class Orders
{
    // Use case 7: returns a result the caller must look at ([MustObserve], RF0001).
    public Result<Unit, OrderError> Cancel(int id) =>
        Get(id).Map(_ => Unit.Value);
}

internal sealed class Baskets(Orders orders, ILogger<Baskets> log)
{
    private readonly Orders _orders = orders;
    private readonly ILogger _log = log;
    private readonly IReadOnlyList<Basket> _abandoned = [new Basket(1), new Basket(2)];

    public IReadOnlyList<Basket> Abandoned() => _abandoned;

    // The fix for use case 7: decide what a failure means here. tests/Resultful.EndToEnd/Broken/DiscardedResult is the
    // version that throws the result away.
    public void CancelAbandonedBaskets()
    {
        foreach (var basket in Abandoned())
        {
            #region snippet: uc07-fix
            _orders.Cancel(basket.OrderId)
                   .TeeError(e => _log.LogWarning("could not cancel {Id}: {Error}", basket.OrderId, e.Message));
            #endregion
        }
    }
}
