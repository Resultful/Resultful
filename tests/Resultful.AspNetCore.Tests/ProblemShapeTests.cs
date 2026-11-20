using System.Diagnostics;
using System.Text.Json;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

// The body is RFC 9457 ProblemDetails, written by ASP.NET Core's IProblemDetailsService.
public class ProblemShapeTests
{
    [Fact]
    public async Task A_mapped_4xx_has_type_title_status_detail_instance_and_trace_id()
    {
        var response = await Execute(Result.Fail<int>(new OrderNotFound(999)).ToHttpResult(), configure: MapOrders, path: "/api/orders/999");
        Assert.Equal(404, response.Status);
        Assert.Equal("application/problem+json", response.ContentType);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", response.Get("type"));
        Assert.Equal("Not Found", response.Get("title"));
        Assert.Equal(404, response.Body.GetProperty("status").GetInt32());
        Assert.Equal("order 999 was not found", response.Get("detail"));
        Assert.Equal("/api/orders/999", response.Get("instance"));
        Assert.Equal("trace-1", response.Get("traceId"));
    }

    [Fact]
    public async Task The_instance_is_path_base_and_path_without_the_query()
    {
        var response = await Execute(Result.Fail<int>(new OrderNotFound(9)).ToHttpResult(), configure: MapOrders, path: "/orders/9",
            request: c => { c.Request.PathBase = "/shop"; c.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?email=ada@example.com"); });
        Assert.Equal("/shop/orders/9", response.Get("instance"));
        Assert.DoesNotContain("ada@example.com", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_409_has_the_conflict_title()
    {
        var response = await Execute(Result.Fail<int>(new OrderLocked(2, "alice")).ToHttpResult(), configure: MapOrders);
        Assert.Equal("Conflict", response.Get("title"));
        Assert.Equal("order 2 is locked by alice", response.Get("detail"));
    }

    [Fact]
    public async Task The_detail_is_the_matched_errors_message_not_the_wrappers()
    {
        // The wrapper's message may hold internal text; the mapped error is the one the app chose to show.
        var error = new SaveFailed(1) { Cause = new OrderLocked(1, "ann") }.WithContext("db write 42");
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders);
        Assert.Equal("order 1 is locked by ann", response.Get("detail"));
        Assert.DoesNotContain("not saved", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("db write", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_and_leaf_properties_are_not_in_the_body()
    {
        var error = new OrderLocked(5, "ann").WithContext("internal note");
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders);
        Assert.DoesNotContain("internal note", response.Text, StringComparison.Ordinal);
        Assert.False(response.Body.TryGetProperty("by", out _));
        Assert.False(response.Body.TryGetProperty("context", out _));
    }

    [Fact]
    public async Task The_trace_id_is_the_current_activity_when_there_is_one()
    {
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var response = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: MapOrders);
        Assert.Equal(activity.Id, response.Get("traceId"));
    }

    [Fact]
    public async Task Without_the_problem_details_service_the_same_body_is_written()
    {
        var registered = await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult());
        var bare = await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), register: false);
        Assert.Equal(500, bare.Status);
        Assert.Equal("application/problem+json", bare.ContentType);
        Assert.True(JsonElement.DeepEquals(registered.Body, bare.Body), registered.Text + " vs " + bare.Text);
    }

    [Fact]
    public async Task A_request_that_does_not_accept_json_still_gets_the_status()
    {
        var response = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: MapOrders,
            request: c => c.Request.Headers.Accept = "text/html");
        Assert.Equal(404, response.Status);
    }
}
