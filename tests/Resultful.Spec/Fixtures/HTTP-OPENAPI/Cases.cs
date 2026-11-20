// M5 (20b): a result's metadata lists the success status and each failure status, so the OpenAPI document shows them.
// spec: uses Resultful.AspNetCore
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Resultful.AspNetCore;
using Resultful.Spec.Support;

public sealed record Order(int Id);

public static class Cases
{
    public static ResultHttpResult<Order> Handler() => throw new System.NotImplementedException();

    [SpecCase]
    public static void Metadata_lists_every_status()
    {
        var builder = new RouteEndpointBuilder(_ => System.Threading.Tasks.Task.CompletedTask, RoutePatternFactory.Parse("/orders/{id}"), 0);
        ResultHttpResult<Order>.PopulateMetadata(typeof(Cases).GetMethod(nameof(Handler))!, builder);
        var statuses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().Select(m => m.StatusCode).Order().ToArray();
        Check.True(statuses.Contains(200) && statuses.Contains(400) && statuses.Contains(500), "statuses: " + string.Join(", ", statuses));
        Check.True(builder.Metadata.OfType<IProducesResponseTypeMetadata>().Single(m => m.StatusCode == 200).Type == typeof(Order), "200 is the Order");
    }
}
