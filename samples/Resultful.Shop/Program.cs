using Resultful.Shop;

// `scenarios` prints every scenario line for every pricing style; anything else runs the shop's HTTP endpoints.
if (args is ["scenarios"])
{
    foreach (var style in (PricingStyle[])[PricingStyle.Open, PricingStyle.Closed, PricingStyle.Shaped])
    {
        foreach (var line in await Scenarios.RunAsync(style, CancellationToken.None).ConfigureAwait(false))
        {
            Console.WriteLine($"{style} {line}");
        }
    }

    return 0;
}

await using var app = ShopApp.Create(args);
await app.RunAsync().ConfigureAwait(false);
return 0;
