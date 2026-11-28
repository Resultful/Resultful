using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Resultful.AspNetCore;
using Resultful.Text.Json;

namespace Resultful.Shop;

/// <summary>The value types and leaves, source-generated: no reflection-based metadata anywhere.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(OrderRequest))]
[JsonSerializable(typeof(SkuNotFound))]
[JsonSerializable(typeof(InvalidField))]
[JsonSerializable(typeof(OverLimit))]
internal sealed partial class ShopJson : JsonSerializerContext;

public static class ShopJsonOptions
{
    /// <summary>Web options that read and write the shop's results, natively compiled.</summary>
    public static JsonSerializerOptions Create() => Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>
    /// Puts the result metadata and the context first in <paramref name="options"/>' resolver chain and registers the
    /// leaves. [JsonSerializable(typeof(Result&lt;decimal&gt;))] is not an option: it calls the parameterless constructor,
    /// which is an error (CS0619), so each result type gets converter-backed metadata from a small resolver instead.
    /// </summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.TypeInfoResolverChain.Insert(0, ShopJson.Default);
        options.TypeInfoResolverChain.Insert(0, new ResultMetadata());
        return options.AddErrorCases<Error>(ShopError.Cases).AddErrorCases<ShopError>(ShopError.Cases);
    }

    private sealed class ResultMetadata : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(Result<decimal>) ? JsonMetadataServices.CreateValueInfo<Result<decimal>>(options, new ResultJsonConverter<decimal>())
            : type == typeof(Result<decimal, ShopError>) ? JsonMetadataServices.CreateValueInfo<Result<decimal, ShopError>>(options, new ResultJsonConverter<decimal, ShopError>())
            : null;
    }
}

/// <summary>The shop over HTTP: each route is a pipeline that ends in ToHttpResult.</summary>
public static class ShopApp
{
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        configure?.Invoke(builder);
        builder.Services.ConfigureHttpJsonOptions(o => ShopJsonOptions.Configure(o.SerializerOptions));
        builder.Services.AddResultful(errors => errors
            .Map<SkuNotFound>(StatusCodes.Status404NotFound)
            .Map<InvalidField>(StatusCodes.Status400BadRequest)
            .Map<OverLimit>(StatusCodes.Status422UnprocessableEntity));

        var app = builder.Build();
        app.MapGet("/quote/{sku}", (string sku, string qty) => new OpenPricing().Place(sku, qty).ToHttpResult());
        app.MapGet("/closed/quote/{sku}", async (string sku, string qty, CancellationToken ct) =>
            (await new ClosedPricing().PlaceAsync(sku, qty, ct).ConfigureAwait(false)).ToHttpResult());
        app.MapPost("/orders", (OrderRequest request) => new ClosedPricing().Check(request).ToHttpResult());
        return app;
    }
}
