// Stand-in for Resultful.Generators. Deleted in 13b.
#nullable enable
namespace Resultful.Shop.Domain.Fulfilment;

public abstract partial record FulfilmentError
{
    private protected FulfilmentError(string message) : base(message) { }

    public static IReadOnlyList<Type> Cases { get; } = [typeof(AddressInvalid), typeof(OrderNotFound), typeof(StockUnavailable), typeof(WarehouseOffline)];

    // Walks error.Causes() from the outer error inwards and maps the first case found. Cases most-derived first,
    // ties by fully qualified name.
    public static bool TryMap<TResult>(
        Error error,
        Func<AddressInvalid, TResult> onAddressInvalid,
        Func<OrderNotFound, TResult> onOrderNotFound,
        Func<StockUnavailable, TResult> onStockUnavailable,
        Func<WarehouseOffline, TResult> onWarehouseOffline,
        [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TResult result)
    {
        ArgumentNullException.ThrowIfNull(error);
        foreach (var e in error.Causes())
        {
            switch (e)
            {
                case AddressInvalid a: result = onAddressInvalid(a); return true;
                case OrderNotFound n: result = onOrderNotFound(n); return true;
                case StockUnavailable s: result = onStockUnavailable(s); return true;
                case WarehouseOffline w: result = onWarehouseOffline(w); return true;
            }
        }

        result = default;
        return false;
    }
}
