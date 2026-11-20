using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

// Use case 10, after: samples/Resultful.Shop.Legacy/UC10 is the string-error code this replaces.
internal static class MigratedOrders
{
    private static readonly OrderStore Store = new();

    #region snippet: uc10-after
    public static Result<Order, OrderError> Get(int id) =>
        Store.Find(id) is { } o ? o : new OrderNotFound(id);

    public static IResult Respond(Result<Order, OrderError> result) =>
        result.Match(
            o => Results.Ok(o),
            error => error switch { OrderNotFound => Results.NotFound(), OrderLocked => Results.Conflict() });
    #endregion
}
