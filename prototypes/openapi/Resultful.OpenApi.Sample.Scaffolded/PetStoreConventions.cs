// Scaffolded from petstore.yaml by Resultful.OpenApi.Scaffold. This file is yours: edit it freely, it is never
// regenerated. The contract it implements (the types, unions and handler interfaces) stays generated.
#nullable enable

using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using PetStore.Api;

namespace PetStore.Scaffolded;

public static class PetStoreConventions
{
    /// <summary>
    /// JSON as the spec reads it (numbers are numbers; a oneOf discriminator may come anywhere), for both MVC and the
    /// OpenAPI document; and request validation from the spec's constraints rather than MVC's automatic 400.
    /// </summary>
    public static IServiceCollection AddPetStoreConventions(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
        });
        services.Configure<JsonOptions>(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.JsonSerializerOptions.AllowOutOfOrderMetadataProperties = true;
        });
        services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        // MVC's JSON input also accepts application/*+json and text/json; the spec documents application/json only.
        services.Configure<MvcOptions>(options =>
        {
            foreach (var json in options.InputFormatters.OfType<SystemTextJsonInputFormatter>())
            {
                json.SupportedMediaTypes.Remove("application/*+json");
                json.SupportedMediaTypes.Remove("text/json");
            }
        });
        return services;
    }
}
