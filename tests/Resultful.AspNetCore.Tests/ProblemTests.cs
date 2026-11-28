using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

// A failed validation answers 400 with a validation problem, one entry per key.
public class ValidationTests
{

    [Fact]
    public async Task A_failed_validation_is_a_400_with_every_error()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name"), new Required("email") });
        var response = await Execute(v.ToHttpResult(), configure: ByField);
        Assert.Equal(400, response.Status);
        Assert.Equal("application/problem+json", response.ContentType);
        Assert.Equal("One or more validation errors occurred.", response.Get("title"));
        Assert.Equal(400, response.Body.GetProperty("status").GetInt32());
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.Equal("email is required", response.Body.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("trace-1", response.Get("traceId"));
        Assert.Null(response.Get("detail"));
    }

    [Fact]
    public async Task Errors_with_one_key_are_listed_together_in_order()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("password"), new TooShort("password", 12), new Required("name") });
        var errors = (await Execute(v.ToHttpResult(), configure: ByField)).Body.GetProperty("errors");
        Assert.Equal(["password is required", "password needs at least 12 characters"], errors.GetProperty("password").EnumerateArray().Select(e => e.GetString()));
        Assert.Single(errors.GetProperty("name").EnumerateArray());
    }

    [Fact]
    public async Task The_default_key_is_the_error_type_name()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name"), new Required("email"), new TooShort("password", 12) });
        var errors = (await Execute(v.ToHttpResult())).Body.GetProperty("errors");
        Assert.Equal(2, errors.GetProperty("Required").GetArrayLength());
        Assert.Equal(1, errors.GetProperty("TooShort").GetArrayLength());
    }

    [Fact]
    public async Task A_typed_validation_is_a_400()
    {
        var v = Validation.Fail<int, Required>(new[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: ByField);
        Assert.Equal(400, response.Status);
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task ValidationFailed_inside_a_result_is_a_400_too()
    {
        Result<int> r = Validation.Fail<int>(new Error[] { new Required("name") }).ToResult();
        Assert.Equal(400, (await Execute(r.ToHttpResult())).Status);
        Result<int> typed = Validation.Fail<int, Required>(new[] { new Required("name") }).ToResult();
        Assert.Equal(400, (await Execute(typed.ToHttpResult())).Status);
    }

    [Fact]
    public async Task ValidationFailed_as_a_cause_is_not_a_400_by_default()
    {
        // The built-in 400 is for the result's own error. A server error wrapping a validation failure stays a 500
        // and shows none of its messages.
        var error = new SaveFailed(1) { Cause = new ValidationFailed<Error>([new Required("name")]) };
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: ByField);
        Assert.Equal(500, response.Status);
        Assert.DoesNotContain("name is required", response.Text, StringComparison.Ordinal);
        Assert.False(response.Body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ValidationFailed_as_a_cause_is_a_400_when_mapped()
    {
        var error = new SaveFailed(1) { Cause = new ValidationFailed<Error>([new Required("name")]) };
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: o =>
        {
            ByField(o);
            o.Map<ValidationFailed<Error>>(StatusCodes.Status400BadRequest);
        });
        Assert.Equal(400, response.Status);
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task A_context_note_keeps_the_built_in_400()
    {
        Result<int> r = Validation.Fail<int>(new Error[] { new Required("name") }).ToResult();
        var noted = Result.Fail<int>(r.Error.WithContext("sign-up"));
        Assert.Equal(400, (await Execute(noted.ToHttpResult())).Status);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_ValidationFailed_mapped_to_a_5xx_shows_no_messages(int status)
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: o => o.Map<ValidationFailed<Error>>(status));
        Assert.Equal(status, response.Status);
        Assert.False(response.Body.TryGetProperty("errors", out _));
        Assert.Null(response.Get("detail"));
        Assert.DoesNotContain("name is required", response.Text, StringComparison.Ordinal);
    }

    private sealed record Missing<TKey>(TKey Key) : Error($"{Key} is missing");

    [Fact]
    public async Task A_generic_leaf_is_keyed_by_its_wire_name()
    {
        // Proposed decision 25: the key JSON writes as "type", not the CLR name Missing`1.
        var v = Validation.Fail<int>(new Error[] { new Missing<int>(7) });
        var response = await Execute(v.ToHttpResult());
        Assert.Equal("7 is missing", response.Body.GetProperty("errors").GetProperty("Missing<Int32>")[0].GetString());
    }

    [Fact]
    public async Task A_null_key_is_an_empty_key()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: o => o.ValidationKey = _ => null!);
        Assert.Equal(400, response.Status);
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("")[0].GetString());
    }

    [Fact]
    public async Task A_validation_error_that_is_also_mapped_is_still_listed_not_mapped()
    {
        // The ValidationFailed is met first in the walk, so the 400 wins over its inner errors' own entries.
        var v = Validation.Fail<int>(new Error[] { new OrderNotFound(1) });
        Assert.Equal(400, (await Execute(v.ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task An_entry_for_the_exact_ValidationFailed_type_changes_its_status_not_its_body()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: o => o.Map<ValidationFailed<Error>>(StatusCodes.Status422UnprocessableEntity));
        Assert.Equal(422, response.Status);
        Assert.Equal(1, response.Body.GetProperty("errors").GetProperty("Required").GetArrayLength());
    }

    [Fact]
    public async Task An_entry_for_Error_does_not_take_a_ValidationFailed()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        Assert.Equal(400, (await Execute(v.ToHttpResult(), configure: o => o.Map<Error>(StatusCodes.Status418ImATeapot))).Status);
        Assert.Equal(418, (await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), configure: o => o.Map<Error>(StatusCodes.Status418ImATeapot))).Status);
    }

    [Fact]
    public async Task A_validation_message_is_shown_in_production()
    {
        var v = Validation.Fail<int>(new Error[] { new TooShort("password", 12) });
        Assert.Contains("needs at least 12", (await Execute(v.ToHttpResult())).Text, StringComparison.Ordinal);
    }
}

// A 5xx has a fixed title and no Message outside Development, and is logged instead.
public class NoLeakTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task Outside_development_a_500_says_nothing_about_the_error(string environment)
    {
        var error = new DbTimeout("select * from orders where id = 42") { Cause = Error.From("Password=hunter2 rejected") }.WithContext("loading order 42");
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), environment);
        Assert.Equal(500, response.Status);
        Assert.Equal(ServerErrorTitle, response.Get("title"));
        Assert.Null(response.Get("detail"));
        foreach (var secret in new[] { "hunter2", "timed out", "where id", "loading order 42", "DbTimeout" })
            Assert.DoesNotContain(secret, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_a_500_shows_the_error_its_context_and_its_cause()
    {
        var error = new DbTimeout("q1") { Cause = Error.From("db rejected") }.WithContext("loading order 42");
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), "Development");
        Assert.Equal(500, response.Status);
        Assert.Equal(ServerErrorTitle, response.Get("title"));
        var detail = response.Get("detail")!;
        Assert.Contains("timed out running q1", detail, StringComparison.Ordinal);
        Assert.Contains("loading order 42", detail, StringComparison.Ordinal);
        Assert.Contains("db rejected", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_host_environment_counts_as_production()
    {
        var services = new ServiceCollection().AddLogging().AddResultful();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        await Result.Fail<int>("Password=hunter2").ToHttpResult().ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mapped_5xx_hides_the_message_too()
    {
        var response = await Execute(Result.Fail<int>(new FraudSuspected(97)).ToHttpResult(),
            configure: o => o.Map<FraudSuspected>(StatusCodes.Status503ServiceUnavailable));
        Assert.Equal(503, response.Status);
        Assert.Null(response.Get("detail"));
        Assert.DoesNotContain("risk score", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_4xx_default_status_shows_no_message()
    {
        var response = await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), configure: o => o.DefaultStatusCode = StatusCodes.Status400BadRequest);
        Assert.Equal(400, response.Status);
        Assert.Null(response.Get("detail"));
        Assert.DoesNotContain("not saved", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hidden_5xx_is_logged_once_at_error_without_leaf_properties()
    {
        var log = new LogSink();
        var error = new CredentialRejected("hunter2-secret") { Cause = Error.From("db said no") }.WithContext("loading order 42");
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), log: log);
        Assert.Equal(500, response.Status);

        var entry = Assert.Single(log.Entries, e => e.Category == "Resultful.AspNetCore.ResultHttpResult");
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.Equal("ResultServerError", entry.Id.Name);
        Assert.Contains("500", entry.Message, StringComparison.Ordinal);
        Assert.Contains("CredentialRejected", entry.Message, StringComparison.Ordinal);
        Assert.Contains("the credential was rejected", entry.Message, StringComparison.Ordinal);
        Assert.Contains("loading order 42", entry.Message, StringComparison.Ordinal);
        Assert.Contains("db said no", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2-secret", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("hunter2-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_4xx_is_not_logged_by_the_package()
    {
        var log = new LogSink();
        await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: MapOrders, log: log);
        Assert.DoesNotContain(log.Entries, e => e.Category == "Resultful.AspNetCore.ResultHttpResult");
    }

    [Fact]
    public async Task A_mapped_4xx_shows_its_message_in_production()
    {
        var response = await Execute(Result.Fail<int>(new CardDeclined()).ToHttpResult(), configure: o => o.Map<CardDeclined>(409));
        Assert.Equal("The card was declined.", response.Get("detail"));
    }

    [Fact]
    public async Task A_4xx_is_the_same_in_development_and_production()
    {
        var production = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: MapOrders);
        var development = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), "Development", MapOrders);
        Assert.Equal(production.Text, development.Text);
    }
}

// Nothing this package writes depends on the current culture: keys, titles and statuses are the same under tr-TR,
// where "I".ToLower() is "ı".
public class CultureTests
{
    static async Task<string[]> Bodies()
    {
        var v = Validation.Fail<int>(new Error[] { new InvalidId(1), new InvalidId(2) });
        return
        [
            (await Execute(v.ToHttpResult())).Text,
            (await Execute(Result.Fail<int>(new InvalidId(3)).ToHttpResult(), configure: o => o.Map<InvalidId>(400))).Text,
            (await Execute(Result.Fail<int>(new InvalidId(4)).ToHttpResult())).Text,
            (await Execute(Result.Ok(new Order(1, "İstanbul")).ToHttpResult())).Text,
        ];
    }

    [Fact]
    public async Task Bodies_are_the_same_under_tr_TR_and_invariant()
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var invariant = await Bodies();
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = await Bodies();
            Assert.Equal(invariant, turkish);
            Assert.Contains("\"InvalidId\"", turkish[0], StringComparison.Ordinal);
            Assert.Contains("ID 3 is invalid", turkish[1], StringComparison.Ordinal);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }
}
