using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CsCheck;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

// Stack PR 24d: Result<IAsyncEnumerable<T>> over HTTP. The checks run before the first item, so a failure answers the
// mapped ProblemDetails before any byte; a success streams a JSON array as the items arrive; a failure after the first
// item can only end the response. Scenarios on TestServer, read incrementally; laws on DefaultHttpContext.
public sealed class StreamTests
{
    public sealed record Shipment(int OrderId, string TrackingCode);

    public sealed class BoomException(string message) : Exception(message);

    /// <summary>A feed of orders that records what its consumer did: how far it read, its token, whether its finally ran.</summary>
    public sealed class Feed
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource> _ended = new(StringComparer.Ordinal);

        /// <summary>Completes when the request for <paramref name="path"/> has left the whole pipeline, logging included.</summary>
        public Task Ended(string path) => _ended.GetOrAdd(path, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void End(string path) => _ended.GetOrAdd(path, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        public int Produced { get; private set; }
        public CancellationToken Token { get; private set; }

        // mode: "gated" waits for Release before the second item; "fails" waits for Release (the client has read the
        // first item), then throws;
        // "fails-at-once" throws right after the first, before the serializer has sent anything; "throws" throws before
        // the first; "endless" waits forever (until cancelled) after the first; "empty" yields nothing.
        public async IAsyncEnumerable<Order> Orders(string mode, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Token = ct;
            try
            {
                await Task.Yield();
                if (mode == "throws") throw new BoomException("SECRET-before the first order");
                if (mode == "empty") yield break;
                Produced++;
                yield return new Order(1, "teapot");
                if (mode == "fails-at-once") throw new BoomException("SECRET-the store went away");
                if (mode == "fails")
                {
                    await Release.Task.WaitAsync(ct);
                    throw new BoomException("SECRET-the store went away");
                }
                if (mode == "gated") await Release.Task.WaitAsync(ct);
                if (mode == "endless") await Task.Delay(Timeout.Infinite, ct);
                Produced++;
                yield return new Order(2, "kettle");
                Produced++;
                yield return new Order(3, "cup");
            }
            finally
            {
                Finished.TrySetResult();
            }
        }

        // The checks run first: a missing feed or a rejected credential fails before any order is read.
        public async Task<Result<IAsyncEnumerable<Order>, OrderError>> Find(string mode, CancellationToken ct)
        {
            await Task.Yield();
            return mode == "missing" ? new OrderNotFound(9) : Result.Ok<IAsyncEnumerable<Order>, OrderError>(Orders(mode, ct));
        }

        // An error union as the error: a declined card fails before any order is read.
        public Result<IAsyncEnumerable<Order>, PaymentError> FindPayment(string mode, CancellationToken ct) =>
            mode == "declined" ? new PaymentError(new CardDeclined()) : Result.Ok<IAsyncEnumerable<Order>, PaymentError>(Orders(mode, ct));

        public async Task<Result<IAsyncEnumerable<Order>>> FindAny(string mode, CancellationToken ct) =>
            (await Find(mode, ct)).Match(Result.Ok, error => Result.Fail<IAsyncEnumerable<Order>>(error));
    }

    static Validation<string> CheckMode(string mode) => mode.Length < 3 ? new TooShort("mode", 3) : mode;

    static async Task<(WebApplication App, Feed Feed)> Start(LogSink? log = null, bool snakeCase = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (log is not null) builder.Logging.AddProvider(log);
        if (snakeCase) builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
        builder.Services.AddResultful(o => o.Map<OrderNotFound>(StatusCodes.Status404NotFound).Map<CardDeclined>(StatusCodes.Status402PaymentRequired));
        builder.Services.AddResultful(Http.ByField);
        var feed = new Feed();
        builder.Services.AddSingleton(feed);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            finally { feed.End(context.Request.Path); }
        });
        app.UseExceptionHandler();

        // Validation → a domain error from an async store → a streamed body.
        app.MapGet("/api/feeds/{mode}", async (string mode, Feed feed, CancellationToken ct) =>
            (await CheckMode(mode).ToResult().BindAsync(feed.FindAny, ct)).ToHttpResult());
        app.MapGet("/api/typed/{mode}", async (string mode, Feed feed, CancellationToken ct) => (await feed.Find(mode, ct)).ToHttpResult());
        app.MapGet("/api/payments/{mode}", (string mode, Feed feed, CancellationToken ct) => feed.FindPayment(mode, ct).ToHttpResult());
        app.MapGet("/api/rejected", () => Result.Fail<IAsyncEnumerable<Order>>(new CredentialRejected("SECRET-hunter2")).ToHttpResult());
        app.MapGet("/api/shipments", () => Result.Ok(Shipments()).ToHttpResult());
        await app.StartAsync();
        return (app, feed);

        static async IAsyncEnumerable<Shipment> Shipments()
        {
            await Task.Yield();
            yield return new Shipment(1, "TRK-1");
        }
    }

    static async Task<(HttpStatusCode Status, string? ContentType, string Text)> Get(WebApplication app, string path)
    {
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Reads <paramref name="body"/> until <paramref name="until"/> holds for what has arrived, or the body ends.</summary>
    static async Task<string> ReadUntil(Stream body, StringBuilder text, Func<string, bool> until)
    {
        var buffer = new byte[256];
        while (!until(text.ToString()))
        {
            var n = await body.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);
            if (n == 0) break;
            text.Append(Encoding.UTF8.GetString(buffer, 0, n));
        }

        return text.ToString();
    }

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static JsonElement Json(string text) => JsonElement.Parse(text);

    // ---- Before the first item ----

    [Fact]
    public async Task A_failure_answers_the_mapped_problem_before_any_item_is_read()
    {
        var (app, feed) = await Start();
        await using var _ = app;

        var missing = await Get(app, "/api/feeds/missing");
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal("application/problem+json", missing.ContentType);
        Assert.Equal("order 9 was not found", Json(missing.Text).GetProperty("detail").GetString());
        Assert.Equal("/api/feeds/missing", Json(missing.Text).GetProperty("instance").GetString());

        var invalid = await Get(app, "/api/feeds/ab");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        Assert.Equal("mode needs at least 3 characters", Json(invalid.Text).GetProperty("errors").GetProperty("mode")[0].GetString());

        var typed = await Get(app, "/api/typed/missing");
        Assert.Equal(HttpStatusCode.NotFound, typed.Status);
        Assert.Equal(0, feed.Produced);
    }

    [Fact]
    public async Task No_status_answer_shows_a_leaf_errors_own_properties()
    {
        var log = new LogSink();
        var (app, feed) = await Start(log);
        await using var _ = app;

        var rejected = await Get(app, "/api/rejected");
        await feed.Ended("/api/rejected");
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.Status);
        Assert.Equal(ServerErrorTitle, Json(rejected.Text).GetProperty("title").GetString());
        Assert.DoesNotContain("SECRET", rejected.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", rejected.Text, StringComparison.Ordinal);
        Assert.All(log.Snapshot(), e => Assert.DoesNotContain("SECRET", e.Message, StringComparison.Ordinal));
        Assert.Single(log.Snapshot(), e => e.Category == "Resultful.AspNetCore.ResultHttpResult" && e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task An_exception_before_the_first_item_reaches_the_exception_handler_with_the_response_not_started()
    {
        var log = new LogSink();
        var (app, feed) = await Start(log);
        await using var _ = app;

        // Before the first item, or right after it while the serializer still holds everything: nothing was sent.
        foreach (var mode in (string[])["throws", "fails-at-once"])
        {
            var thrown = await Get(app, $"/api/feeds/{mode}");
            await feed.Ended($"/api/feeds/{mode}");
            Assert.Equal(HttpStatusCode.InternalServerError, thrown.Status);
            Assert.Equal("application/problem+json", thrown.ContentType);
            Assert.DoesNotContain("SECRET", thrown.Text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(log.Snapshot(), e => e.Category == StreamCategory);
        await feed.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    const string StreamCategory = "Resultful.AspNetCore.StreamHttpResult";

    // ---- Streaming ----

    [Fact]
    public async Task A_success_streams_the_array_as_the_items_arrive()
    {
        var (app, feed) = await Start();
        await using var _ = app;
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(new Uri("/api/feeds/gated", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        // The first order arrives while the producer is still waiting to make the second.
        await using var body = await response.Content.ReadAsStreamAsync(Ct);
        var text = new StringBuilder();
        var first = await ReadUntil(body, text, t => t.Contains('}', StringComparison.Ordinal));
        Assert.StartsWith("""[{"id":1,"name":"teapot"}""", first, StringComparison.Ordinal);
        Assert.Equal(1, feed.Produced);

        feed.Release.SetResult();
        var all = await ReadUntil(body, text, _ => false);
        Assert.Equal("""[{"id":1,"name":"teapot"},{"id":2,"name":"kettle"},{"id":3,"name":"cup"}]""", all);
        await feed.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task A_result_whose_error_is_an_error_union_streams_with_the_same_guarantees()
    {
        var (app, feed) = await Start();
        await using var _ = app;

        var declined = await Get(app, "/api/payments/declined");
        Assert.Equal(HttpStatusCode.PaymentRequired, declined.Status);
        Assert.Equal("The card was declined.", Json(declined.Text).GetProperty("detail").GetString());

        var thrown = await Get(app, "/api/payments/throws");
        Assert.Equal((HttpStatusCode.InternalServerError, "application/problem+json"), (thrown.Status, thrown.ContentType));

        feed.Release.SetResult();
        var streamed = await Get(app, "/api/payments/gated");
        Assert.Equal((HttpStatusCode.OK, "application/json"), (streamed.Status, streamed.ContentType));
        Assert.Equal(3, Json(streamed.Text).GetArrayLength());

        StreamHttpResult<Order, Error> union = Result.Ok<IAsyncEnumerable<Order>, PaymentError>(feed.Orders("empty", Ct)).ToHttpResult();
        Assert.NotNull(union);
        Assert.Throws<InvalidOperationException>(() => default(Result<IAsyncEnumerable<Order>, PaymentError>).ToHttpResult());
    }

    [Fact]
    public async Task An_empty_stream_answers_an_empty_array_and_the_app_json_options_apply()
    {
        var (app, _) = await Start();
        await using (app)
        {
            var empty = await Get(app, "/api/feeds/empty");
            Assert.Equal((HttpStatusCode.OK, "application/json", "[]"), empty);
        }

        var (snake, _) = await Start(snakeCase: true);
        await using (snake)
        {
            var shipments = await Get(snake, "/api/shipments");
            Assert.Equal("""[{"order_id":1,"tracking_code":"TRK-1"}]""", shipments.Text);
        }
    }

    [Fact]
    public async Task A_failure_after_the_first_item_ends_the_response_and_is_logged_without_a_problem()
    {
        var log = new LogSink();
        var (app, feed) = await Start(log);
        await using var _ = app;
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(new Uri("/api/feeds/fails", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The client reads the first order; only then does the producer fail. It sees the start of the array, then a read
        // error: never a ProblemDetails.
        await using var body = await response.Content.ReadAsStreamAsync(Ct);
        var text = new StringBuilder();
        await ReadUntil(body, text, t => t.Contains('}', StringComparison.Ordinal));
        feed.Release.SetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => ReadUntil(body, text, _ => false));
        Assert.StartsWith("""[{"id":1,"name":"teapot"}""", text.ToString(), StringComparison.Ordinal);
        Assert.False(text.ToString().EndsWith(']'));
        Assert.DoesNotContain("title", text.ToString(), StringComparison.Ordinal);

        await feed.Ended("/api/feeds/fails");
        var entry = Assert.Single(log.Snapshot(), e => e.Category == StreamCategory);
        Assert.Equal((LogLevel.Warning, "ResultStreamFailed", "A streamed result failed after 1 items were handed to the serializer; the response was aborted."), (entry.Level, entry.Id.Name, entry.Message));
        Assert.DoesNotContain("SECRET", entry.Message, StringComparison.Ordinal);
        await feed.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task A_client_that_goes_away_cancels_the_producer()
    {
        var log = new LogSink();
        var (app, feed) = await Start(log);
        await using var _ = app;
        using var client = app.GetTestClient();
        var response = await client.GetAsync(new Uri("/api/feeds/endless", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, Ct);
        var body = await response.Content.ReadAsStreamAsync(Ct);
        await ReadUntil(body, new StringBuilder(), t => t.Contains('}', StringComparison.Ordinal));

        await body.DisposeAsync();
        response.Dispose();
        await feed.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await feed.Ended("/api/feeds/endless");
        Assert.True(feed.Token.IsCancellationRequested);
        Assert.Equal(1, feed.Produced);
        Assert.DoesNotContain(log.Snapshot(), e => e.Category == StreamCategory);
    }

    // ---- The type, metadata and laws ----

    [Fact]
    public void ToHttpResult_picks_the_stream_overload_for_a_result_that_holds_a_stream()
    {
        var feed = new Feed();
        StreamHttpResult<Order, Error> plain = Result.Ok(feed.Orders("empty", Ct)).ToHttpResult();
        StreamHttpResult<Order, OrderError> typed = Result.Ok<IAsyncEnumerable<Order>, OrderError>(feed.Orders("empty", Ct)).ToHttpResult();
        ResultHttpResult<Order, Error> single = Result.Ok(new Order(1, "teapot")).ToHttpResult();
        Assert.NotNull(plain);
        Assert.NotNull(typed);
        Assert.NotNull(single);
        Assert.Throws<InvalidOperationException>(() => default(Result<IAsyncEnumerable<Order>>).ToHttpResult());
        Assert.Throws<InvalidOperationException>(() => default(Result<IAsyncEnumerable<Order>, OrderError>).ToHttpResult());
    }

    static Dictionary<int, Type?> Metadata<TError>() where TError : Error
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/feeds"), 0);
        StreamHttpResult<Order, TError>.PopulateMetadata(typeof(StreamTests).GetMethod(nameof(Metadata), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!, builder);
        return builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToDictionary(m => m.StatusCode, m => m.Type);
    }

    [Fact]
    public async Task The_endpoint_metadata_lists_a_streamed_array_and_the_problems()
    {
        Assert.Equal(new Dictionary<int, Type?> { [200] = typeof(IAsyncEnumerable<Order>), [500] = typeof(ProblemDetails) }, Metadata<OrderError>());
        Assert.Equal(new Dictionary<int, Type?> { [200] = typeof(IAsyncEnumerable<Order>), [400] = typeof(HttpValidationProblemDetails), [500] = typeof(ProblemDetails) }, Metadata<Error>());

        var (app, _) = await Start();
        await using var __ = app;
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        int[] Statuses(string pattern) => [.. endpoints.Single(e => e.RoutePattern.RawText == pattern)
            .Metadata.OfType<IProducesResponseTypeMetadata>().Select(m => m.StatusCode).Distinct().Order()];
        Assert.Equal([200, 400, 500], Statuses("/api/feeds/{mode}"));
        Assert.Equal([200, 500], Statuses("/api/typed/{mode}"));
    }

    static async IAsyncEnumerable<Order> Items(IEnumerable<Order> orders)
    {
        foreach (var order in orders)
        {
            await Task.Yield();
            yield return order;
        }
    }

    static readonly Gen<Order> Orders = Gen.Select(Gen.Int[1, 999], Gen.String[Gen.Char['a', 'z'], 0, 8], (id, name) => new Order(id, name));

    // A failure: mapped, under a wrapper, a validation failure, or unmapped with a secret in a leaf property.
    static readonly Gen<Error> Errors = Gen.Frequency(
        (2, Gen.Int[1, 999].Select(id => (Error)new OrderNotFound(id))),
        (1, Gen.Int[1, 999].Select(id => (Error)new SaveFailed(id) { Cause = new OrderLocked(id, "ann") })),
        (1, Gen.Int[3, 9].Select(min => (Error)new ValidationFailed<Error>([new TooShort("mode", min)]))),
        (1, Gen.String[Gen.Char['a', 'z'], 1, 8].Select(s => (Error)new CredentialRejected("SECRET-" + s))));

    [Fact]
    public Task A_failed_stream_writes_exactly_what_a_failed_result_writes() =>
        Errors.SampleAsync(async error =>
        {
            var streamed = await Execute(Result.Fail<IAsyncEnumerable<Order>>(error).ToHttpResult(), configure: MapOrders);
            var single = await Execute(Result.Fail<Order>(error).ToHttpResult(), configure: MapOrders);
            Assert.Equal(single, streamed);
            Assert.DoesNotContain("SECRET", streamed.Text, StringComparison.Ordinal);
        }, iter: 60);

    [Fact]
    public Task A_streamed_success_writes_the_items_as_the_serializer_writes_the_list() =>
        Orders.List[0, 6].SampleAsync(async orders =>
        {
            var streamed = await Execute(Result.Ok(Items(orders)).ToHttpResult());
            Assert.Equal(200, streamed.Status);
            Assert.Equal("application/json; charset=utf-8", streamed.ContentType);
            Assert.Equal(JsonSerializer.Serialize(orders, JsonSerializerOptions.Web), streamed.Text);
        }, iter: 40);

    [Fact]
    public async Task Without_a_server_an_exception_before_the_first_item_writes_nothing()
    {
        var feed = new Feed();
        var boom = await Assert.ThrowsAsync<BoomException>(() => Execute(Result.Ok(feed.Orders("throws", Ct)).ToHttpResult()));
        Assert.StartsWith("SECRET", boom.Message, StringComparison.Ordinal);
        Assert.True(feed.Finished.Task.IsCompleted);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Result.Ok(feed.Orders("empty", Ct)).ToHttpResult().ExecuteAsync(null!));
    }
}
