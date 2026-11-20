// Native AOT probe (see README.md). Each route exercises one path of Resultful.AspNetCore: a success value written
// through a source-generated context, a mapped 404, a hidden 500 and a 400 validation problem.
using System.Text.Json.Serialization;
using Resultful;
using Resultful.AspNetCore;
using Resultful.AspNetCore.AotProbe;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ProbeJson.Default));
builder.Services.AddResultful(o => o.Map<OrderNotFound>(StatusCodes.Status404NotFound));
var app = builder.Build();

app.MapGet("/orders/{id}", (int id) => (id == 1 ? Result.Ok(new Order(1, "teapot")) : Result.Fail<Order>(new OrderNotFound(id))).ToHttpResult());
app.MapGet("/boom", () => Result.Fail<Order>("Password=hunter2").ToHttpResult());
app.MapGet("/check", () => Validation.Fail<Order>(new Error[] { new OrderNotFound(1) }).ToHttpResult());
app.Run();

namespace Resultful.AspNetCore.AotProbe
{
    public sealed record Order(int Id, string Name);

    public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");

    [JsonSerializable(typeof(Order))]
    internal sealed partial class ProbeJson : JsonSerializerContext;
}
