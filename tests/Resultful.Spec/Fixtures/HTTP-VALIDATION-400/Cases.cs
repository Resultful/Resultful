// M5 (20a): ValidationFailed answers 400 with a validation problem, one entry per key.
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

public sealed record Required(string Field) : Error($"{Field} is required");

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
    public static async Task A_failed_validation_is_a_400()
    {
        Validation<int> v = Validation.Fail<int>(new Error[] { new Required("name"), new Required("email") });
        var (status, body) = await Execute(v.ToHttpResult(), configure: o => o.ValidationKey = e => e is Required r ? r.Field : "");
        Check.Equal(400, status, "status");
        Check.Equal("name is required", body.GetProperty("errors").GetProperty("name")[0].GetString(), "name");
        Check.Equal("email is required", body.GetProperty("errors").GetProperty("email")[0].GetString(), "email");
    }

    [SpecCase]
    public static async Task ValidationFailed_inside_a_Result_is_a_400_too()
    {
        Result<int> r = Validation.Fail<int>(new Error[] { new Required("name") }).ToResult();
        var (status, _) = await Execute(r.ToHttpResult());
        Check.Equal(400, status, "status");
    }
}
