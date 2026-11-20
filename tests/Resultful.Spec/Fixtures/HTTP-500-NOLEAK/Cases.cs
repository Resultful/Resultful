// Quality (20a): a 500 body has a fixed title and no Message outside Development, so internal details don't leak.
// spec: uses Resultful.AspNetCore
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resultful;
using Resultful.AspNetCore;
using Resultful.Spec.Support;

public static class Cases
{
    static async Task<(int Status, JsonElement Body)> Execute(IResult result, string environment = "Production", System.Action<ResultfulHttpOptions>? configure = null)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails();
        services.AddSingleton<IHostEnvironment>(new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = environment });
        if (configure is null) services.AddResultful(); else services.AddResultful(configure);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = context.Response.Body.Length == 0 ? default : JsonDocument.Parse(context.Response.Body).RootElement.Clone();
        return (context.Response.StatusCode, body);
    }

    [SpecCase]
    public static async Task Production_hides_the_message()
    {
        var (status, body) = await Execute(Result.Fail<int>("connection string Password=hunter2 rejected").ToHttpResult());
        Check.Equal(500, status, "status");
        Check.False(body.GetRawText().Contains("hunter2", System.StringComparison.Ordinal), "no message: " + body.GetRawText());
        Check.Equal("An error occurred while processing your request.", body.GetProperty("title").GetString(), "fixed title");
    }

    [SpecCase]
    public static async Task Development_shows_it()
    {
        var (_, body) = await Execute(Result.Fail<int>("db rejected").ToHttpResult(), "Development");
        Check.True(body.GetRawText().Contains("db rejected", System.StringComparison.Ordinal), "message in Development");
    }
}
