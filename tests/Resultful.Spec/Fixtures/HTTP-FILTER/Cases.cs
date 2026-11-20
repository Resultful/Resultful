// M5 (20b): the endpoint filter lets a Minimal API handler return a result directly.
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
    public static async Task A_returned_result_becomes_an_http_result()
    {
        var filter = new ResultEndpointFilter();
        var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());
        var returned = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Result.Fail<int>("nope")));
        Check.True(returned is IResult, "an IResult: " + returned?.GetType().Name);
        Check.Equal(500, (await Execute((IResult)returned!)).Status, "mapped");
    }
}
