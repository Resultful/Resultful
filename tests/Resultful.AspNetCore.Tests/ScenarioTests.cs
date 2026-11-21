using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Resultful.Text.Json;

namespace Resultful.AspNetCore.Tests;

// A Minimal API shop on TestServer: routing, the ProblemDetails service, logging and the environment are ASP.NET
// Core's own. Each route is a pipeline: validation, then domain errors from an async store, then HTTP.
public sealed class ScenarioTests
{
    public sealed record PlaceOrder(string Name, string Email);

    public sealed record BatchReport(int Saved, IReadOnlyList<Result<Order>> Items);

    /// <summary>An async store whose failures are wrapped the way a data layer wraps them: a cause, a context note.</summary>
    public sealed class OrderStore
    {
        public Task<Result<Order, OrderError>> Find(int id, CancellationToken ct) => Task.FromResult<Result<Order, OrderError>>(id switch
        {
            1 => new Order(1, "teapot"),
            2 => new OrderLocked(2, "alice"),
            _ => new OrderNotFound(id),
        });

        public async Task<Result<Order>> Save(PlaceOrder order, CancellationToken ct)
        {
            await Task.Yield();
            return order.Name switch
            {
                "locked" => Result.Fail<Order>(new SaveFailed(7) { Cause = new OrderLocked(7, "bob") }.WithContext("saving the order")),
                "slow" => Result.Fail<Order>(new DbTimeout("insert into orders values ('slow', 'ada@example.com')") { Cause = Error.From("Password=hunter2 rejected") }.WithContext("saving the order")),
                _ => Result.Ok(new Order(7, order.Name)),
            };
        }
    }

    static Validation<PlaceOrder> Check(PlaceOrder r)
    {
        var errors = new List<Error>();
        if (r.Name.Length == 0) errors.Add(new Required("name"));
        if (r.Email.Length == 0) errors.Add(new Required("email"));
        else if (r.Email.Length < 6) errors.Add(new TooShort("email", 6));
        return errors.Count == 0 ? Validation.Ok(r) : Validation.Fail<PlaceOrder>(errors);
    }

    static async Task<WebApplication> Start(string environment, LogSink? log = null, bool resultfulJson = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (log is not null) builder.Logging.AddProvider(log);
        if (resultfulJson) builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.AddResultful());
        builder.Services.AddSingleton<OrderStore>();
        builder.Services.AddResultful(o => o.Map<OrderNotFound>(StatusCodes.Status404NotFound).Map<OrderLocked>(StatusCodes.Status409Conflict));
        builder.Services.AddResultful(Http.ByField);
        var app = builder.Build();

        app.MapGet("/api/orders/{id}", async (int id, OrderStore store, CancellationToken ct) => (await store.Find(id, ct)).ToHttpResult());
        app.MapPost("/api/orders", async (PlaceOrder body, OrderStore store, CancellationToken ct) =>
            (await Check(body).ToResult().BindAsync(store.Save, ct)).ToHttpResult());
        app.MapPost("/api/orders/check", (PlaceOrder body) => Check(body).ToHttpResult());
        app.MapPost("/api/orders/batch", async (PlaceOrder[] body, OrderStore store, CancellationToken ct) =>
        {
            var items = new List<Result<Order>>();
            foreach (var order in body) items.Add(await Check(order).ToResult().BindAsync(store.Save, ct));
            var saved = items.Count(i => i.IsSuccess);
            return (saved == 0 ? Result.Fail<BatchReport>(new BatchFailed([.. items.Select(i => i.Error)])) : Result.Ok(new BatchReport(saved, items))).ToHttpResult();
        });
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

    static JsonElement Json(string text) => JsonElement.Parse(text);

    [Fact]
    public async Task Reading_an_order_answers_200_404_and_409()
    {
        await using var app = await Start("Production");

        var ok = await Send(app, HttpMethod.Get, "/api/orders/1");
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal("teapot", Json(ok.Text).GetProperty("name").GetString());

        var notFound = await Send(app, HttpMethod.Get, "/api/orders/999");
        Assert.Equal(HttpStatusCode.NotFound, notFound.Status);
        Assert.Equal("application/problem+json", notFound.ContentType);
        var problem = Json(notFound.Text);
        Assert.Equal("Not Found", problem.GetProperty("title").GetString());
        Assert.Equal("order 999 was not found", problem.GetProperty("detail").GetString());
        Assert.Equal("/api/orders/999", problem.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));

        var locked = await Send(app, HttpMethod.Get, "/api/orders/2");
        Assert.Equal(HttpStatusCode.Conflict, locked.Status);
        Assert.Equal("Conflict", Json(locked.Text).GetProperty("title").GetString());
        Assert.Equal("order 2 is locked by alice", Json(locked.Text).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Placing_an_order_goes_through_validation_the_store_and_the_map()
    {
        var log = new LogSink();
        await using var app = await Start("Production", log);

        var placed = await Send(app, HttpMethod.Post, "/api/orders", new PlaceOrder("teapot", "ada@example.com"));
        Assert.Equal(HttpStatusCode.OK, placed.Status);
        Assert.Equal(7, Json(placed.Text).GetProperty("id").GetInt32());

        // Validation: every field's errors, keyed by field; nothing reaches the store.
        var invalid = await Send(app, HttpMethod.Post, "/api/orders", new PlaceOrder("", "ada"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        var errors = Json(invalid.Text).GetProperty("errors");
        Assert.Equal("One or more validation errors occurred.", Json(invalid.Text).GetProperty("title").GetString());
        Assert.Equal("name is required", errors.GetProperty("name")[0].GetString());
        Assert.Equal("email needs at least 6 characters", errors.GetProperty("email")[0].GetString());

        // A domain error under the store's wrapper: the cause is mapped, and only its message is shown.
        var locked = await Send(app, HttpMethod.Post, "/api/orders", new PlaceOrder("locked", "ada@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, locked.Status);
        Assert.Equal("order 7 is locked by bob", Json(locked.Text).GetProperty("detail").GetString());
        Assert.DoesNotContain("saving the order", locked.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("not saved", locked.Text, StringComparison.Ordinal);

        // An infrastructure error: a fixed 500, logged once with the whole error, its body saying nothing.
        var slow = await Send(app, HttpMethod.Post, "/api/orders", new PlaceOrder("slow", "ada@example.com"));
        Assert.Equal(HttpStatusCode.InternalServerError, slow.Status);
        Assert.Equal(Http.ServerErrorTitle, Json(slow.Text).GetProperty("title").GetString());
        Assert.False(Json(slow.Text).TryGetProperty("detail", out _));
        foreach (var secret in new[] { "hunter2", "insert into", "ada@example.com", "saving the order", "DbTimeout" })
            Assert.DoesNotContain(secret, slow.Text, StringComparison.Ordinal);

        var entry = Assert.Single(log.Entries, e => e.Category == "Resultful.AspNetCore.ResultHttpResult");
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("saving the order", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Password=hunter2 rejected", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_a_500_shows_the_error()
    {
        await using var app = await Start("Development");
        var slow = await Send(app, HttpMethod.Post, "/api/orders", new PlaceOrder("slow", "ada@example.com"));
        Assert.Equal(HttpStatusCode.InternalServerError, slow.Status);
        var detail = Json(slow.Text).GetProperty("detail").GetString();
        Assert.Contains("saving the order", detail, StringComparison.Ordinal);
        Assert.Contains("timed out running", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resultful_Text_Json_in_the_apps_options_writes_values_and_leaves_problems_alone()
    {
        await using var app = await Start("Production", resultfulJson: true);

        // A success value holding results uses Resultful's wire format.
        var mixed = await Send(app, HttpMethod.Post, "/api/orders/batch", new[] { new PlaceOrder("teapot", "ada@example.com"), new PlaceOrder("", "ada@example.com") });
        Assert.Equal(HttpStatusCode.OK, mixed.Status);
        var items = Json(mixed.Text).GetProperty("items");
        Assert.True(items[0].GetProperty("ok").GetBoolean());
        Assert.False(items[1].GetProperty("ok").GetBoolean());
        Assert.Equal("name is required", items[1].GetProperty("error").GetProperty("errors")[0].GetProperty("message").GetString());

        // A failure is still a ProblemDetails, mapped through BatchFailed's inner errors, not a Resultful error.
        var failed = await Send(app, HttpMethod.Post, "/api/orders/batch", new[] { new PlaceOrder("locked", "ada@example.com") });
        Assert.Equal(HttpStatusCode.Conflict, failed.Status);
        Assert.Equal("application/problem+json", failed.ContentType);
        var problem = Json(failed.Text);
        Assert.Equal("order 7 is locked by bob", problem.GetProperty("detail").GetString());
        Assert.False(problem.TryGetProperty("ok", out _));
        Assert.False(problem.TryGetProperty("type", out var type) && type.GetString() == "BatchFailed");
    }

    [Fact]
    public async Task The_endpoint_metadata_comes_from_the_return_type()
    {
        await using var app = await Start("Production");
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        int[] Statuses(string pattern) => [.. endpoints.Single(e => e.RoutePattern.RawText == pattern)
            .Metadata.OfType<IProducesResponseTypeMetadata>().Select(m => m.StatusCode).Distinct().Order()];

        Assert.Equal([200, 500], Statuses("/api/orders/{id}"));
        Assert.Equal([200, 400, 500], Statuses("/api/orders"));
        Assert.Equal([200, 400, 500], Statuses("/api/orders/check"));
    }
}
