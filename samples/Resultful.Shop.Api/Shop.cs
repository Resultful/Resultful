#if SHOP_UC01 || SHOP_UC02 || SHOP_UC03 || SHOP_UC04 || SHOP_UC05
using Resultful.AspNetCore;
#endif

namespace Resultful.Shop.Api;

internal static class Shop
{
    public static IServiceCollection AddShop(this IServiceCollection services)
    {
#if SHOP_UC01 || SHOP_UC02 || SHOP_UC03 || SHOP_UC04 || SHOP_UC05
        // The HTTP map (DECISIONS-TAKEN, 04c): ValidationFailed is 400, a mapped type its status, anything else 500.
        services.AddResultful(http =>
        {
#if SHOP_UC01
            OrderEndpoints.MapErrors(http);
#endif
#if SHOP_UC02
            SignUpEndpoints.MapErrors(http);
#endif
#if SHOP_UC03
            RegisterEndpoints.MapErrors(http);
#endif
#if SHOP_UC05
            PaymentEndpoints.MapErrors(http);
#endif
        });
#endif
#if SHOP_UC01
        // Factories rather than AddSingleton<T>(), so the analyzers see each type constructed (CA1812).
        services.AddSingleton(_ => new Orders());
        services.AddSingleton(_ => new ShapedOrders());
#endif
#if SHOP_UC07
        services.AddSingleton(sp => new Baskets(sp.GetRequiredService<Orders>(), sp.GetRequiredService<ILogger<Baskets>>()));
#endif
#if SHOP_UC03
        services.AddSingleton(_ => new Registration());
#endif
#if SHOP_UC04
        services.AddSingleton(sp => new Invoicing(sp.GetRequiredService<ILogger<Invoicing>>()));
#endif
#if SHOP_UC05
        services.AddSingleton(_ => new Payments());
#endif
        return services;
    }

    public static WebApplication MapShop(this WebApplication app)
    {
#if SHOP_UC01
        OrderEndpoints.Map(app);
#endif
#if SHOP_UC02
        SignUpEndpoints.Map(app);
#endif
#if SHOP_UC03
        RegisterEndpoints.Map(app);
#endif
#if SHOP_UC04
        InvoiceEndpoints.Map(app);
#endif
#if SHOP_UC05
        PaymentEndpoints.Map(app);
#endif
#if SHOP_UC07
        app.MapPost("/baskets/cancel-abandoned", (Baskets baskets) => baskets.CancelAbandonedBaskets());
#endif
        return app;
    }
}
