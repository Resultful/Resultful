using System.Globalization;
using Resultful.CodeAnalysis;

namespace Resultful.Shop;

public sealed record Product(string Sku, decimal Price);

public sealed record Line(Product Product, int Qty);

public sealed record OrderRequest(string Sku, string Qty, string Note);

public sealed record Order(string Sku, int Qty, decimal Total, string Note);

/// <summary>Everything the shop can refuse. Closed: a switch over it with no discard arm is exhaustive (RFSP0001).</summary>
[ClosedHierarchy]
public abstract partial record ShopError(string Message) : Error(Message);

public sealed record SkuNotFound(string Sku) : ShopError($"{Sku} is not in the catalog");

public sealed record InvalidField(string Field, string Rule) : ShopError($"{Field} fails {Rule}");

public sealed record OverLimit(decimal Total) : ShopError($"{Total.ToString("0.00", CultureInfo.InvariantCulture)} is over the order limit");

/// <summary>The priced line as a named result: one Match handler per leaf, and on .NET 11 an exhaustive switch.</summary>
[ResultShape(typeof(ShopError), Success = typeof(decimal))]
public readonly partial struct PriceResult;

public static class Catalog
{
    public const decimal Limit = 1000m;

    public const int MaxNote = 40;

    public static readonly IReadOnlyDictionary<string, decimal> Prices = new Dictionary<string, decimal>
    {
        ["SKU-00001"] = 2.50m,
        ["SKU-00002"] = 10.00m,
    };

    public static readonly IReadOnlyDictionary<string, decimal> Discounts = new Dictionary<string, decimal>
    {
        ["SAVE10"] = 0.10m,
    };

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A stable code per leaf, written as a switch with no discard arm.</summary>
    public static string Code(this ShopError error) => error switch
    {
        SkuNotFound => "sku.unknown",
        InvalidField f => $"{f.Field}.{f.Rule}",
#if !BREAK_ARM
        OverLimit => "price.limit",
#endif
    };

    /// <summary>The code of any error: a shop error's own, or the message of one from elsewhere.</summary>
    public static string Code(this Error error) => error is ShopError shop ? shop.Code() : error.Message;

    public static Option<decimal> Discount(string code) =>
        Discounts.TryGetValue(code, out var rate) ? Option.Some(rate) : Option<decimal>.None;

    public static Validation<string, ShopError> Note(string note) =>
        note.Length > MaxNote ? new InvalidField("note", "length") : note;
}
