// M5 (20a): the user's map. The error is walked outside-in (the error, its Cause chain, Inner errors); the first mapped type wins.
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

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id) : Error($"order {Id} is locked");
public sealed record SaveFailed(int Id) : Error($"order {Id} was not saved");

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

    static void Map(ResultfulHttpOptions o) => o.Map<OrderNotFound>(StatusCodes.Status404NotFound).Map<OrderLocked>(StatusCodes.Status409Conflict);

    [SpecCase]
    public static async Task Each_mapped_type_gets_its_status()
    {
        Check.Equal(404, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: Map)).Status, "404");
        Check.Equal(409, (await Execute(Result.Fail<int>(new OrderLocked(1).WithContext("note")).ToHttpResult(), configure: Map)).Status, "a note changes nothing");
        Check.Equal(409, (await Execute(Result.Fail<int>(new SaveFailed(1) { Cause = new OrderLocked(1) }).ToHttpResult(), configure: Map)).Status, "the cause, when the wrapper isn't mapped");
        Check.Equal(200, (await Execute(Result.Ok(1).ToHttpResult(), configure: Map)).Status, "success");
    }
}
