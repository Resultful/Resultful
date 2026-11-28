using System.Text.Json;

namespace Resultful.Shop;

public enum PricingStyle
{
    Open,
    Closed,
    Shaped,
}

/// <summary>
/// The acceptance scenarios: one "ID: value" line each, the same lines for every pricing style. The tests compare
/// them in process; the packed-package leg compares a native AOT build's output with them.
/// </summary>
public static class Scenarios
{
    public static readonly IReadOnlyList<string> Expected =
    [
        "S01: ok:SKU-00002@10.00",
        "S02: err:sku.unknown",
        "S03: ok:10.00 steps=3",
        "S04: err:sku.unknown steps=2",
        "S05: ok:20.00|err:QTY.RANGE",
        "S06: ok:10.00|err:price.limit",
        "S07: errs:qty.format,sku.unknown,note.length|ok",
        "S08: NotFound:SKU-99999|Invalid:qty/format|Limit:2000.00",
        "S09: some:0.10|none",
        "S10: ok:null",
        "S11: eq:True|False|True",
        "S12: back:ok:10.00|err:sku.unknown",
        "S13: ctx:quoting found:SKU-99999",
        "S14: err:qty.format|ok:4",
        "S15: match:NotFound,Invalid,Limit switch:NotFound,Invalid,Limit trymap:True,False",
        "S16: ok:10.00,Limit:2000.00 get:True,True,True,False ctx:pricing has:True,True",
        "S17: switch:ok:Ada,missing:C-9,suspended:C-2,unverified:C-3 match:Customer,CustomerNotFound,AccountError,AccountError",
        "S18: widen:Ada,CustomerNotFound,Suspended,Unverified ctx:checkout",
        "S19: get:True,False,True json:Ada,CustomerNotFound,AccountError.Suspended,AccountError.Unverified",
        "N01: throws:InvalidOperationException",
        "N02: ok:hello",
        "N02x: ok:hello",
        "N03: throws:InvalidOperationException",
        "N04: throws:InvalidOperationException",
        "N05: default:True has:False throws:InvalidOperationException",
    ];

    private const string LongNote = "a note that is much longer than the forty characters allowed";

    public static async Task<IReadOnlyList<string>> RunAsync(PricingStyle style, CancellationToken ct)
    {
        var lines = new List<string>();
        void Add(string id, Func<string> run) => lines.Add($"{id}: {Guard(run)}");

        Add("S01", () => OpenPricing.Lookup("SKU-00002").Match(p => $"ok:{p.Sku}@{Catalog.Money(p.Price)}", e => $"err:{e.Code()}"));
        Add("S02", () => OpenPricing.Lookup("SKU-99999").Match(p => $"ok:{p.Sku}", e => $"err:{e.Code()}"));
        Add("S03", () => Place(style, "SKU-00001", "4"));
        Add("S04", () => Place(style, "SKU-99999", "4"));
        Add("S05", () => MapBoth(style));
        lines.Add($"S06: {await PlaceAsync(style, "SKU-00001", "4", ct).ConfigureAwait(false)}|{await PlaceAsync(style, "SKU-00002", "200", ct).ConfigureAwait(false)}");
        Add("S07", () => $"{Check(new OrderRequest("SKU-99999", "x", LongNote))}|{Check(new OrderRequest("SKU-00001", "4", "fine"))}");
        Add("S08", () => Describe(style));
        Add("S09", () => $"{Discount("SAVE10")}|{Discount("NOPE")}");
        Add("S10", () => Result.Ok<string?>(null).Match(v => $"ok:{v ?? "null"}", e => $"err:{e.Code()}"));
        Add("S11", () =>
        {
            Result<decimal> a = 10m, b = 10m, c = new SkuNotFound("a"), d = new SkuNotFound("a");
            return $"eq:{a == b}|{a == c}|{c == d}";
        });
        Add("S12", () => RoundTrip(style));
        Add("S13", () =>
        {
            var error = new OpenPricing().Place("SKU-99999", "4").WithContext("quoting").Error;
            return error.TryFind<SkuNotFound>(out var found) ? $"ctx:{string.Join(',', error.Context)} found:{found.Sku}" : "ctx:none";
        });
        Add("S14", () => string.Join('|',
            Show(Result.Try(() => int.Parse("x", System.Globalization.CultureInfo.InvariantCulture), _ => new InvalidField("qty", "format"))),
            Show(Result.Try(() => int.Parse("4", System.Globalization.CultureInfo.InvariantCulture), _ => new InvalidField("qty", "format")))));
        Add("S15", Generated);
        Add("S16", Shape);
        Add("S17", Nested);
        Add("S18", Widened);
        Add("S19", NestedJson);
        Add("N01", () =>
        {
#if !BREAK_DEFAULT
#pragma warning disable RF0002 // the zero value is the point of this check
#endif
            Result<decimal> zero = default;
#pragma warning restore RF0002
            return Show(zero);
        });
        Add("N02", () => Result.Ok("hello").Match(v => $"ok:{v}", e => $"err:{e.Code()}"));
        Add("N02x", () =>
        {
            Result<string> r = "hello";
            return r.Match(v => $"ok:{v}", e => $"err:{e.Code()}");
        });
        Add("N03", () =>
        {
#if !BREAK_VALUE
#pragma warning disable RF0003 // reading a failure's value is the point of this check
#endif
            return $"ok:{new OpenPricing().Place("SKU-99999", "4").Value}";
#pragma warning restore RF0003
        });
        Add("N04", () =>
        {
#pragma warning disable RF0002 // the zero value is the point of this check
            Result<decimal, ShopError> zero = default;
#pragma warning restore RF0002
            return Show(zero);
        });
        Add("N05", () =>
        {
#pragma warning disable RF0002 // the zero value is the point of this check
            PriceResult zero = default;
#pragma warning restore RF0002
            return $"default:{zero.IsDefault} has:{zero.HasValue} {Guard(() => ShapedPricing.Show(zero))}";
        });
#if BREAK_DROP
        new OpenPricing().Place("SKU-00001", "4");
#endif
        return lines;
    }

    private static string Guard(Func<string> run)
    {
        try
        {
            return run();
        }
#pragma warning disable CA1031 // a scenario that throws is recorded, not fatal
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return $"throws:{ex.GetType().Name}";
        }
    }

    private static string Show<T>(Result<T> r) => r.Match(v => $"ok:{(v is decimal m ? Catalog.Money(m) : v)}", e => $"err:{e.Code()}");

    private static string Show(Result<decimal, ShopError> r) => r.Match(v => $"ok:{Catalog.Money(v)}", e => $"err:{e.Code()}");

    private static string Place(PricingStyle style, string sku, string qty)
    {
        switch (style)
        {
            case PricingStyle.Open:
                var open = new OpenPricing();
                return $"{Show(open.Place(sku, qty))} steps={open.Steps}";
            case PricingStyle.Closed:
                var closed = new ClosedPricing();
                return $"{Show(closed.Place(sku, qty))} steps={closed.Steps}";
            default:
                var shaped = new ShapedPricing();
                return $"{ShapedPricing.Show(shaped.Place(sku, qty))} steps={shaped.Steps}";
        }
    }

    private static async Task<string> PlaceAsync(PricingStyle style, string sku, string qty, CancellationToken ct) => style switch
    {
        PricingStyle.Open => Show(await new OpenPricing().PlaceAsync(sku, qty, ct).ConfigureAwait(false)),
        PricingStyle.Closed => Show(await new ClosedPricing().PlaceAsync(sku, qty, ct).ConfigureAwait(false)),
        _ => ShapedPricing.Show(await new ShapedPricing().PlaceAsync(sku, qty, ct).ConfigureAwait(false)),
    };

    // Map the value, then map the error to its upper-cased code.
    private static string MapBoth(PricingStyle style) => style switch
    {
        PricingStyle.Open => $"{Show(new OpenPricing().Place("SKU-00001", "4").Map(v => v * 2))}|"
            + new OpenPricing().Place("SKU-00001", "0").MapError(e => Error.From(e.Code().ToUpperInvariant())).Match(v => "ok", e => $"err:{e.Message}"),
        PricingStyle.Closed => $"{Show(new ClosedPricing().Place("SKU-00001", "4").Map(v => v * 2))}|"
            + new ClosedPricing().Place("SKU-00001", "0").MapError(e => e.Code().ToUpperInvariant()).Match(v => "ok", e => $"err:{e}"),
        _ => $"{Show(new ShapedPricing().Place("SKU-00001", "4").Map(v => v * 2))}|"
            + new ShapedPricing().Place("SKU-00001", "0").AsResult().MapError(e => e.Code().ToUpperInvariant()).Match(v => "ok", e => $"err:{e}"),
    };

    private static string Check(OrderRequest request) =>
        new ClosedPricing().Check(request).Match(o => "ok", errors => "errs:" + string.Join(',', errors.Select(e => e.Code())));

    // Each kind of error, matched in the style's exhaustive form. The open style's Error is open: it needs the cast.
    private static string Describe(PricingStyle style)
    {
        ShopError[] errors = [new SkuNotFound("SKU-99999"), new InvalidField("qty", "format"), new OverLimit(2000m)];
        return string.Join('|', errors.Select(e => style switch
        {
            PricingStyle.Open => Describe((Error)e),
            PricingStyle.Closed => Describe(e),
            _ => Describe((PriceResult)e),
        }));
    }

    private static string Describe(Error error) => error is ShopError shop ? Describe(shop) : $"Other:{error.Message}";

    private static string Describe(ShopError error) => error switch
    {
        SkuNotFound n => $"NotFound:{n.Sku}",
        InvalidField i => $"Invalid:{i.Field}/{i.Rule}",
        OverLimit l => $"Limit:{Catalog.Money(l.Total)}",
    };

    private static string Describe(PriceResult result) => result switch
    {
        decimal total => $"Ok:{Catalog.Money(total)}",
        SkuNotFound n => $"NotFound:{n.Sku}",
        InvalidField i => $"Invalid:{i.Field}/{i.Rule}",
        OverLimit l => $"Limit:{Catalog.Money(l.Total)}",
    };

    // The hierarchy's generated members: one handler per leaf, each naming its type (RF0008), and TryMap for an Error that may not be a shop error.
    private static string Generated()
    {
        ShopError[] errors = [new SkuNotFound("SKU-99999"), new InvalidField("qty", "format"), new OverLimit(2000m)];
        var matched = errors.Select(e => e.Match((InvalidField i) => "Invalid", (OverLimit o) => "Limit", (SkuNotFound n) => "NotFound"));
        var switched = new List<string>();
        foreach (var e in errors) e.Switch((InvalidField i) => switched.Add("Invalid"), (OverLimit o) => switched.Add("Limit"), (SkuNotFound n) => switched.Add("NotFound"));
        var mapped = new[] { (Error)new OverLimit(1m), Error.From("elsewhere") }
            .Select(e => ShopError.TryMap(e, (InvalidField i) => 0, (OverLimit o) => 1, (SkuNotFound n) => 2, out _));
        return $"match:{string.Join(',', matched)} switch:{string.Join(',', switched)} trymap:{string.Join(',', mapped)}";
    }

    // The shape's generated members: factories, a handler per leaf, typed reads and context. HasValue is the union's:
    // true for a failure too, false only for the default.
    private static string Shape()
    {
        var ok = PriceResult.Ok(10m);
        var over = PriceResult.Fail(new OverLimit(2000m));
        string Match(PriceResult r) => r.Match((decimal v) => Catalog.Money(v), (InvalidField i) => "Invalid", (OverLimit o) => $"Limit:{Catalog.Money(o.Total)}", (SkuNotFound n) => "NotFound");
        bool[] gets = [ok.TryGetValue(out decimal _), over.TryGetError(out _), over.TryGetValue(out OverLimit? _), over.TryGetValue(out SkuNotFound? _)];
        var context = over.WithContext("pricing").TryGetError(out var error) ? string.Join(',', error.Context) : "none";
        return $"ok:{Match(ok)},{Match(over)} get:{string.Join(',', gets)} ctx:{context} has:{ok.HasValue},{over.HasValue}";
    }

    private static readonly string[] CustomerIds = ["C-1", "C-9", "C-2", "C-3"];

    // The nested union through the shape: the generated Match has one handler per direct case, so the inner union
    // arrives whole; the switch reaches its leaves.
    private static string Nested()
    {
        var results = CustomerIds.Select(Customers.Find).ToList();
        var matched = results.Select(r => r.Match((Customer c) => "Customer", (CustomerNotFound n) => "CustomerNotFound", (AccountError a) => "AccountError"));
        return $"switch:{string.Join(',', results.Select(Customers.Describe))} match:{string.Join(',', matched)}";
    }

    // Widened to Result<Customer>, the error is the innermost leaf, and context survives the trip.
    private static string Widened()
    {
        var widened = CustomerIds.Select(id => (Result<Customer>)Customers.Find(id));
        var context = ((Result<Customer>)Customers.Find("C-2").WithContext("checkout")).Error.Context;
        return $"widen:{string.Join(',', widened.Select(r => r.Match(c => c.Name, e => e.GetType().Name)))} ctx:{string.Join(',', context)}";
    }

    // TryGetValue reaches the inner union as one value; JSON writes the innermost leaf and reads it back into the
    // same nesting, so Match still finds Suspended under AccountError.
    private static string NestedJson()
    {
        var suspended = Customers.Find("C-2");
        var get = $"{suspended.TryGetValue(out AccountError a) && a.Value is Suspended},{suspended.TryGetValue(out CustomerNotFound? _)},{Customers.Find("C-9").TryGetValue(out CustomerNotFound? _)}";
        var options = ShopJsonOptions.Create();
        var back = CustomerIds.Select(id => Back(Customers.Find(id).AsResult(), options)).Select(r =>
            r.TryGetValue(out var c) ? c.Name
            : r.TryGetError(out var e) && e.Value is AccountError inner ? $"AccountError.{inner.Value!.GetType().Name}"
            : r.Error.Error.GetType().Name);
        return $"get:{get} json:{string.Join(',', back)}";
    }

    private static string Discount(string code) => Catalog.Discount(code).Match(rate => $"some:{Catalog.Money(rate)}", _ => "none");

    private static string RoundTrip(PricingStyle style)
    {
        var options = ShopJsonOptions.Create();
        if (style == PricingStyle.Open)
        {
            var pricing = new OpenPricing();
            return $"back:{Show(Back(pricing.Place("SKU-00001", "4"), options))}|{Show(Back(pricing.Place("SKU-99999", "4"), options))}";
        }

        Result<decimal, ShopError> ok = style == PricingStyle.Closed ? new ClosedPricing().Place("SKU-00001", "4") : new ShapedPricing().Place("SKU-00001", "4");
        Result<decimal, ShopError> err = style == PricingStyle.Closed ? new ClosedPricing().Place("SKU-99999", "4") : new ShapedPricing().Place("SKU-99999", "4");
        return $"back:{Show(Back(ok, options))}|{Show(Back(err, options))}";
    }

    // Typed metadata from the options: the Serialize(value, options) overloads are not AOT-safe, whatever the resolver.
    private static T Back<T>(T value, JsonSerializerOptions options)
    {
        var info = options.GetTypeInfo<T>();
        return JsonSerializer.Deserialize(JsonSerializer.Serialize(value, info), info)!;
    }
}
