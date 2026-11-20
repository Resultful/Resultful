using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

// HTTP-500-NOLEAK: a 500 has a fixed title and no Message outside Development.
public class NoLeakTests
{
    const string FixedTitle = "An error occurred while processing your request.";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task Outside_development_a_500_says_nothing_about_the_error(string environment)
    {
        var error = new DbTimeout("select * from orders where id = 42") { Cause = Error.From("Password=hunter2 rejected") }.WithContext("loading order 42");
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), environment);
        Assert.Equal(500, response.Status);
        Assert.Equal(FixedTitle, response.Get("title"));
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
        Assert.Equal(FixedTitle, response.Get("title"));
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
