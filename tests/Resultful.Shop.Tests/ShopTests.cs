using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CsCheck;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Resultful.Testing.Json;

namespace Resultful.Shop.Tests;

// The shop end to end: every scenario line in each pricing style, every route's status and body, and laws that hold
// the three styles to the same answers on any input.
public sealed class ShopTests
{
    public static TheoryData<PricingStyle> Styles => [PricingStyle.Open, PricingStyle.Closed, PricingStyle.Shaped];

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Every_scenario_holds(PricingStyle style) =>
        Assert.Equal(Scenarios.Expected, await Scenarios.RunAsync(style, TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_cancelled_token_stops_the_async_chain()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenPricing().PlaceAsync("SKU-00001", "4", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ClosedPricing().PlaceAsync("SKU-00001", "4", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ShapedPricing().PlaceAsync("SKU-00001", "4", cts.Token));
    }

    static async Task<WebApplication> Start()
    {
        var app = ShopApp.Create([], b => b.WebHost.UseTestServer());
        await app.StartAsync();
        return app;
    }

    static async Task<(HttpStatusCode Status, string? ContentType, string Text)> Send(WebApplication app, HttpMethod method, string path, object? body = null)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
    }

    // The trace id differs per request: check it is there, then compare the rest of the problem whole.
    static string WithoutTraceId(string problem)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(problem)!.AsObject();
        Assert.False(string.IsNullOrEmpty(node["traceId"]?.GetValue<string>()));
        node.Remove("traceId");
        return node.ToJsonString();
    }

    [Theory]
    [InlineData("/quote")]
    [InlineData("/closed/quote")]
    public async Task Each_quote_route_answers_every_status(string route)
    {
        await using var app = await Start();

        var ok = await Send(app, HttpMethod.Get, $"{route}/SKU-00001?qty=4");
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        JsonAssert.EqualJson("10.00", ok.Text);

        var missing = await Send(app, HttpMethod.Get, $"{route}/SKU-99999?qty=4");
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal("application/problem+json", missing.ContentType);
        JsonAssert.EqualJson($$"""
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,
             "detail":"SKU-99999 is not in the catalog","instance":"{{route}}/SKU-99999"}
            """, WithoutTraceId(missing.Text));

        var invalid = await Send(app, HttpMethod.Get, $"{route}/SKU-00001?qty=x");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        JsonAssert.EqualJson($$"""
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,
             "detail":"qty fails format","instance":"{{route}}/SKU-00001"}
            """, WithoutTraceId(invalid.Text));

        var over = await Send(app, HttpMethod.Get, $"{route}/SKU-00002?qty=200");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.Status);
        JsonAssert.EqualJson($$"""
            {"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Unprocessable Entity","status":422,
             "detail":"2000.00 is over the order limit","instance":"{{route}}/SKU-00002"}
            """, WithoutTraceId(over.Text));
    }

    [Fact]
    public async Task An_order_is_checked_whole()
    {
        await using var app = await Start();

        var ok = await Send(app, HttpMethod.Post, "/orders", new OrderRequest("SKU-00001", "4", "fine"));
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        JsonAssert.EqualJson("""{"sku":"SKU-00001","qty":4,"total":10.00,"note":"fine"}""", ok.Text);

        var bad = await Send(app, HttpMethod.Post, "/orders", new OrderRequest("SKU-99999", "x", new string('n', Catalog.MaxNote + 1)));
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal("application/problem+json", bad.ContentType);
        JsonAssert.EqualJson("""
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.",
             "status":400,"instance":"/orders",
             "errors":{"InvalidField":["qty fails format","note fails length"],"SkuNotFound":["SKU-99999 is not in the catalog"]}}
            """, WithoutTraceId(bad.Text));
    }

    // Any SKU (known or not) and any quantity text (digits, zero, junk, huge).
    static readonly Gen<string> Skus = Gen.OneOf(Gen.OneOf("SKU-00001", "SKU-00002", "SKU-99999"), Gen.String[0, 12]);
    static readonly Gen<string> Quantities = Gen.OneOf(Gen.Int[0, 600].Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Gen.String[0, 6]);

    static string Show(Result<decimal> r) => r.Match(v => $"ok:{Catalog.Money(v)}", e => $"err:{e.Code()}");

    static string Show(Result<decimal, ShopError> r) => r.Match(v => $"ok:{Catalog.Money(v)}", e => $"err:{e.Code()}");

    [Fact]
    public void The_three_styles_agree_on_any_input() =>
        Gen.Select(Skus, Quantities).Sample((sku, qty) =>
        {
            OpenPricing open = new();
            ClosedPricing closed = new();
            ShapedPricing shaped = new();
            var expected = $"{Show(open.Place(sku, qty))} steps={open.Steps}";
            Assert.Equal(expected, $"{Show(closed.Place(sku, qty))} steps={closed.Steps}");
            Assert.Equal(expected, $"{ShapedPricing.Show(shaped.Place(sku, qty))} steps={shaped.Steps}");
        });

    [Fact]
    public async Task Async_and_sync_chains_agree_on_any_input()
    {
        var ct = TestContext.Current.CancellationToken;
        await Gen.Select(Skus, Quantities).SampleAsync(async (sku, qty) =>
        {
            Assert.Equal(Show(new OpenPricing().Place(sku, qty)), Show(await new OpenPricing().PlaceAsync(sku, qty, ct)));
            Assert.Equal(Show(new ClosedPricing().Place(sku, qty)), Show(await new ClosedPricing().PlaceAsync(sku, qty, ct)));
            Assert.Equal(ShapedPricing.Show(new ShapedPricing().Place(sku, qty)), ShapedPricing.Show(await new ShapedPricing().PlaceAsync(sku, qty, ct)));
        }, iter: 200);
    }

    [Fact]
    public void A_json_round_trip_is_the_identity()
    {
        var options = ShopJsonOptions.Create();
        var open = options.GetTypeInfo<Result<decimal>>();
        var closed = options.GetTypeInfo<Result<decimal, ShopError>>();
        Gen.Select(Skus, Quantities).Sample((sku, qty) =>
        {
            var a = new OpenPricing().Place(sku, qty);
            Assert.Equal(a, JsonSerializer.Deserialize(JsonSerializer.Serialize(a, open), open));
            var b = new ClosedPricing().Place(sku, qty);
            Assert.Equal(b, JsonSerializer.Deserialize(JsonSerializer.Serialize(b, closed), closed));
        });
    }

    [Fact]
    public void Validation_keeps_one_error_per_failed_check() =>
        Gen.Select(Skus, Quantities, Gen.String[0, 60]).Sample((sku, qty, note) =>
        {
            var failed = new[]
            {
                new ClosedPricing().ParseQty(qty).IsFailure,
                ClosedPricing.Lookup(sku).IsFailure,
                note.Length > Catalog.MaxNote,
            }.Count(f => f);
            var result = new ClosedPricing().Check(new OrderRequest(sku, qty, note));
            Assert.Equal(failed, result.Match(_ => 0, errors => errors.Count));
        });
}
