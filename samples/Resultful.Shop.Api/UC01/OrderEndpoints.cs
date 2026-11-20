using Resultful.AspNetCore;
using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

internal static class OrderEndpoints
{
    public static void MapErrors(ResultfulHttpOptions http) => http
        .Map<OrderNotFound>(StatusCodes.Status404NotFound)
        .Map<OrderLocked>(StatusCodes.Status409Conflict);

    public static void Map(WebApplication app)
    {
        #region snippet: uc01-endpoint
        app.MapGet("/orders/{id}", (int id, Orders orders) =>
            orders.Get(id).Match(
                order => Results.Ok(order),
                error => error switch
                {
                    OrderNotFound => Results.NotFound(),
                    OrderLocked l => Results.Conflict(new { l.By }),
                }));
        #endregion

#if NET11_0_OR_GREATER
        #region snippet: uc01-shape-endpoint
        app.MapGet("/shaped/orders/{id}", (int id, ShapedOrders orders) => orders.Get(id) switch
        {
            Order order => Results.Ok(order),
            OrderNotFound => Results.NotFound(),
            OrderLocked l => Results.Conflict(new { l.By }),
        });
        #endregion
#else
        // net10.0: the generated Match does the same job, with typed handlers (RF0008).
        app.MapGet("/shaped/orders/{id}", (int id, ShapedOrders orders) => orders.Get(id).Match(
            (Order order) => Results.Ok(order),
            (OrderLocked l) => Results.Conflict(new { l.By }),
            (OrderNotFound _) => Results.NotFound()));
#endif

        // Through Resultful.AspNetCore: the HTTP map turns each leaf into a ProblemDetails (http-cases.json).
        app.MapGet("/api/orders/{id}", (int id, Orders orders) => orders.Get(id).ToHttpResult());
    }
}
