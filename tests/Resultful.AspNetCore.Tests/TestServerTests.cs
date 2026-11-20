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

namespace Resultful.AspNetCore.Tests;

// A real Minimal API app on TestServer: the pipeline, routing, the ProblemDetails service and the environment are
// ASP.NET Core's own. The bodies follow 04d's http-cases.json for UC01, UC02 and UC04.
public sealed class TestServerTests
{
    static async Task<WebApplication> Start(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddResultful(o => o.Map<OrderNotFound>(StatusCodes.Status404NotFound).Map<OrderLocked>(StatusCodes.Status409Conflict));
        builder.Services.AddResultful(o => o.ValidationKey = e => e is Required r ? r.Field : e is TooShort t ? t.Field : "");
        var app = builder.Build();

        app.MapGet("/api/orders/{id}", (int id) => Get(id).ToHttpResult());
        app.MapGet("/api/invoices/{id}", (int id) => Result.Fail<Order>(new DbTimeout("select 1") { Cause = Error.From("timed out") }.WithContext($"loading order {id}")).ToHttpResult());
        app.MapPost("/api/sign-up/check", (SignUp r) => Check(r).ToHttpResult());
        await app.StartAsync();
        return app;
    }

    public sealed record SignUp(string Name, string Email, string Password);

    static Result<Order, OrderError> Get(int id) => id switch
    {
        1 => new Order(1, "teapot"),
        2 => new OrderLocked(2, "alice"),
        _ => new OrderNotFound(id),
    };

    static Validation<SignUp> Check(SignUp r)
    {
        var errors = new List<Error>();
        if (r.Name.Length == 0) errors.Add(new Required("name"));
        if (r.Password.Length < 12) errors.Add(new TooShort("password", 12));
        return errors.Count == 0 ? Validation.Ok(r) : Validation.Fail<SignUp>(errors);
    }

    static async Task<(HttpStatusCode Status, string? ContentType, JsonElement Body)> Send(WebApplication app, HttpRequestMessage request)
    {
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, text.Length == 0 ? default : JsonElement.Parse(text));
    }

    static Task<(HttpStatusCode Status, string? ContentType, JsonElement Body)> Get(WebApplication app, string path) =>
        Send(app, new HttpRequestMessage(HttpMethod.Get, path));

    [Fact]
    public async Task UC01_200_404_and_409()
    {
        await using var app = await Start("Production");

        var ok = await Get(app, "/api/orders/1");
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal("teapot", ok.Body.GetProperty("name").GetString());

        var notFound = await Get(app, "/api/orders/999");
        Assert.Equal(HttpStatusCode.NotFound, notFound.Status);
        Assert.Equal("application/problem+json", notFound.ContentType);
        Assert.Equal("Not Found", notFound.Body.GetProperty("title").GetString());
        Assert.Equal("order 999 was not found", notFound.Body.GetProperty("detail").GetString());
        Assert.Equal("/api/orders/999", notFound.Body.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrEmpty(notFound.Body.GetProperty("traceId").GetString()));

        var locked = await Get(app, "/api/orders/2");
        Assert.Equal(HttpStatusCode.Conflict, locked.Status);
        Assert.Equal("Conflict", locked.Body.GetProperty("title").GetString());
        Assert.Equal("order 2 is locked by alice", locked.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UC02_a_validation_problem_lists_each_field()
    {
        await using var app = await Start("Production");
        var response = await Send(app, new HttpRequestMessage(HttpMethod.Post, "/api/sign-up/check")
        {
            Content = JsonContent.Create(new SignUp("", "ada@example.com", "short")),
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("One or more validation errors occurred.", response.Body.GetProperty("title").GetString());
        var errors = response.Body.GetProperty("errors");
        Assert.Equal("name is required", errors.GetProperty("name")[0].GetString());
        Assert.Equal("password needs at least 12 characters", errors.GetProperty("password")[0].GetString());
        Assert.False(errors.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task UC04_a_500_hides_everything_in_production()
    {
        await using var app = await Start("Production");
        var response = await Get(app, "/api/invoices/42");
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Equal("An error occurred while processing your request.", response.Body.GetProperty("title").GetString());
        Assert.False(response.Body.TryGetProperty("detail", out _));
        var raw = response.Body.GetRawText();
        Assert.DoesNotContain("timed out", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("loading order 42", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UC04_a_500_shows_the_error_in_development()
    {
        await using var app = await Start("Development");
        var response = await Get(app, "/api/invoices/42");
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Contains("loading order 42", response.Body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_endpoint_metadata_comes_from_the_return_type()
    {
        await using var app = await Start("Production");
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var orders = endpoints.Single(e => e.RoutePattern.RawText == "/api/orders/{id}");
        var statuses = orders.Metadata.OfType<IProducesResponseTypeMetadata>().Select(m => m.StatusCode).Order().ToArray();
        Assert.Equal([200, 500], statuses);
        var check = endpoints.Single(e => e.RoutePattern.RawText == "/api/sign-up/check");
        Assert.Contains(400, check.Metadata.OfType<IProducesResponseTypeMetadata>().Select(m => m.StatusCode));
    }
}
