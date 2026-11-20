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

    // The fix for use case 7: decide what a failure means here. `_ =` is the explicit discard RF0001 accepts; the
    // docs' version without it is still a discarded result (25 updates the doc). tests/Resultful.EndToEnd/Broken/DiscardedResult is the
    // version that throws the result away.
    public void CancelAbandonedBaskets()
    {
        foreach (var basket in Abandoned())
        {
            #region snippet: uc07-fix
            _ = _orders.Cancel(basket.OrderId)
                       .TeeError(e => _log.LogWarning("could not cancel {Id}: {Error}", basket.OrderId, e.Message));
            #endregion
        }
    }

    // The generated Switch, with typed handlers (RF0008): renaming a leaf can't silently swap the two messages.
    public void Report(int orderId, OrderError error) => error.Switch(
        (OrderLocked l) => _log.LogWarning("order {Id} is locked by {By}", orderId, l.By),
        (OrderNotFound _) => _log.LogWarning("order {Id} is gone", orderId));
}
