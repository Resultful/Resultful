using System.Globalization;

namespace Resultful.Shop;

// The same three-step chain (parse the quantity, find the line, price it) written three ways. Steps counts the steps
// that ran, so a test can see that a failure stops the chain. The error type is named only where a style needs it.

/// <summary>Result&lt;T&gt;: the error is any <see cref="Error"/>; a leaf converts on return with no cast.</summary>
public sealed class OpenPricing
{
    public int Steps { get; private set; }

    public Result<int> ParseQty(string text)
    {
        Steps++;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var qty)) return new InvalidField("qty", "format");
        return qty == 0 ? new InvalidField("qty", "range") : qty;
    }

    public static Result<Product> Lookup(string sku) =>
        Catalog.Prices.TryGetValue(sku, out var price) ? new Product(sku, price) : new SkuNotFound(sku);

    public Result<Line> ToLine(string sku, int qty)
    {
        Steps++;
        return Lookup(sku).Map(p => new Line(p, qty));
    }

    public Result<decimal> Price(Line line)
    {
        Steps++;
        var total = line.Product.Price * line.Qty;
        return total > Catalog.Limit ? new OverLimit(total) : total;
    }

    public Result<decimal> Place(string sku, string qty) => ParseQty(qty).Bind(q => ToLine(sku, q)).Bind(Price);

    public async Task<Result<Line>> ToLineAsync(string sku, int qty, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return ToLine(sku, qty);
    }

    public async Task<Result<decimal>> PriceAsync(Line line, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return Price(line);
    }

    // The token goes to the first BindAsync; a Task<Result<T>> has no overload that takes one, so later steps close over it.
    public Task<Result<decimal>> PlaceAsync(string sku, string qty, CancellationToken ct) =>
        ParseQty(qty).BindAsync((q, c) => ToLineAsync(sku, q, c), ct).BindAsync(l => PriceAsync(l, ct));
}

/// <summary>Result&lt;T, ShopError&gt;: the signatures name the closed error, so callers can switch over it exhaustively.</summary>
public sealed class ClosedPricing
{
    public int Steps { get; private set; }

    public Result<int, ShopError> ParseQty(string text)
    {
        Steps++;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var qty)) return new InvalidField("qty", "format");
        return qty == 0 ? new InvalidField("qty", "range") : qty;
    }

    public static Result<Product, ShopError> Lookup(string sku) =>
        Catalog.Prices.TryGetValue(sku, out var price) ? new Product(sku, price) : new SkuNotFound(sku);

    public Result<Line, ShopError> ToLine(string sku, int qty)
    {
        Steps++;
        return Lookup(sku).Map(p => new Line(p, qty));
    }

    public Result<decimal, ShopError> Price(Line line)
    {
        Steps++;
        var total = line.Product.Price * line.Qty;
        return total > Catalog.Limit ? new OverLimit(total) : total;
    }

    public Result<decimal, ShopError> Place(string sku, string qty) => ParseQty(qty).Bind(q => ToLine(sku, q)).Bind(Price);

    public async Task<Result<Line, ShopError>> ToLineAsync(string sku, int qty, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return ToLine(sku, qty);
    }

    public async Task<Result<decimal, ShopError>> PriceAsync(Line line, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return Price(line);
    }

    public Task<Result<decimal, ShopError>> PlaceAsync(string sku, string qty, CancellationToken ct) =>
        ParseQty(qty).BindAsync((q, c) => ToLineAsync(sku, q, c), ct).BindAsync(l => PriceAsync(l, ct));

    /// <summary>The order form: three independent checks, every failure kept, in order.</summary>
    public Validation<Order, ShopError> Check(OrderRequest request)
    {
        Validation<int, ShopError> qty = ParseQty(request.Qty);
        Validation<Product, ShopError> product = Lookup(request.Sku);
        return Validation.Combine(qty, product, Catalog.Note(request.Note),
            (q, p, note) => new Order(p.Sku, q, p.Price * q, note));
    }
}

/// <summary>The generated <see cref="PriceResult"/>: the last step and the chain return the named shape.</summary>
public sealed class ShapedPricing
{
    private readonly ClosedPricing _steps = new();

    public int Steps => _steps.Steps;

    public PriceResult Price(Line line) => _steps.Price(line);

    // Bind's type argument is spelled out: a lambda returning the shape does not infer Result<decimal, ShopError>.
    public PriceResult Place(string sku, string qty) => _steps.ParseQty(qty).Bind(q => _steps.ToLine(sku, q)).Bind<decimal>(l => Price(l));

    public async Task<PriceResult> PlaceAsync(string sku, string qty, CancellationToken ct) =>
        await _steps.ParseQty(qty).BindAsync((q, c) => _steps.ToLineAsync(sku, q, c), ct).BindAsync(l => _steps.PriceAsync(l, ct)).ConfigureAwait(false);

#if NET11_0_OR_GREATER
    /// <summary>A switch over the shape itself, with no discard arm.</summary>
    public static string Show(PriceResult result) => result switch
    {
        decimal total => $"ok:{Catalog.Money(total)}",
        SkuNotFound => "err:sku.unknown",
        InvalidField f => $"err:{f.Field}.{f.Rule}",
        OverLimit => "err:price.limit",
    };
#else
    /// <summary>.NET 10 has no switch over the shape: Match takes one handler per case instead.</summary>
    public static string Show(PriceResult result) => result.Match(
        (decimal total) => $"ok:{Catalog.Money(total)}",
        (InvalidField f) => $"err:{f.Field}.{f.Rule}",
        (OverLimit _) => "err:price.limit",
        (SkuNotFound _) => "err:sku.unknown");
#endif
}
