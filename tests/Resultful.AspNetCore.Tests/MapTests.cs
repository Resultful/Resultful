using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

// HTTP-MAP: the user's map; the error is walked outside-in and the first mapped type wins.
public class MapTests
{
    [Fact]
    public async Task Each_mapped_type_gets_its_status()
    {
        Assert.Equal(404, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: MapOrders)).Status);
        Assert.Equal(409, (await Execute(Result.Fail<int>(new OrderLocked(1, "ann")).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task A_note_changes_nothing()
    {
        var error = new OrderLocked(1, "ann").WithContext("loading the basket").WithContext("checkout");
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task The_cause_answers_when_the_wrapper_is_not_mapped()
    {
        var error = new SaveFailed(1) { Cause = new OrderLocked(1, "ann") };
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task A_deep_cause_answers_too()
    {
        var error = new SaveFailed(1) { Cause = new DbTimeout("q") { Cause = new OrderNotFound(1) } };
        Assert.Equal(404, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task Inner_errors_are_walked()
    {
        var error = new BatchFailed([new DbTimeout("q"), new OrderNotFound(7)]);
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders);
        Assert.Equal(404, response.Status);
        Assert.Equal("order 7 was not found", response.Get("detail"));
    }

    [Fact]
    public async Task The_outermost_mapped_error_wins()
    {
        var error = new OrderLocked(1, "ann") { Cause = new OrderNotFound(1) };
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task The_cause_chain_comes_before_inner_errors()
    {
        // Causes() is depth-first: the error, its Cause chain, then Inner().
        var error = new BatchFailed([new OrderNotFound(1)]) { Cause = new OrderLocked(1, "ann") };
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task An_outer_error_mapped_by_its_base_type_beats_an_exactly_mapped_cause()
    {
        var error = new OrderCancelled(1) { Cause = new SaveFailed(1) };
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(),
            configure: o => o.Map<OrderError>(StatusCodes.Status422UnprocessableEntity).Map<SaveFailed>(StatusCodes.Status503ServiceUnavailable));
        Assert.Equal(422, response.Status);
    }

    [Fact]
    public async Task Anything_unmapped_is_a_500()
    {
        Assert.Equal(500, (await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), configure: MapOrders)).Status);
        Assert.Equal(500, (await Execute(Result.Fail<int>("plain").ToHttpResult())).Status);
        Assert.Equal(500, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult())).Status);
    }

    [Fact]
    public async Task The_default_status_can_change()
    {
        var response = await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), configure: o => o.DefaultStatusCode = StatusCodes.Status502BadGateway);
        Assert.Equal(502, response.Status);
        Assert.Null(response.Get("detail"));
    }

    [Fact]
    public async Task Mapping_a_type_again_replaces_its_status()
    {
        var response = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(),
            configure: o => o.Map<OrderNotFound>(StatusCodes.Status404NotFound).Map<OrderNotFound>(StatusCodes.Status410Gone));
        Assert.Equal(410, response.Status);
    }

    [Fact]
    public void Configure_runs_in_order_and_adds_to_one_map()
    {
        var services = new ServiceCollection();
        services.AddResultful(o => o.Map<OrderNotFound>(404));
        services.AddResultful(o => o.Map<OrderLocked>(409));
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<ResultfulHttpOptions>>().Value;
        Assert.True(options.TryGetStatus(typeof(OrderNotFound), out var a) && a == 404);
        Assert.True(options.TryGetStatus(typeof(OrderLocked), out var b) && b == 409);
    }

    [Fact]
    public async Task A_typed_result_maps_the_same_way()
    {
        Result<Order, OrderError> result = new OrderNotFound(3);
        var response = await Execute(result.ToHttpResult(), configure: MapOrders);
        Assert.Equal(404, response.Status);
        Assert.Equal("order 3 was not found", response.Get("detail"));
    }

    [Fact]
    public async Task Without_registration_the_defaults_apply()
    {
        var response = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), register: false);
        Assert.Equal(500, response.Status);
        Assert.Equal("application/problem+json", response.ContentType);
        Assert.Equal("An error occurred while processing your request.", response.Get("title"));
    }
}
