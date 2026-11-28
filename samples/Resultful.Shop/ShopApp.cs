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
[JsonSerializable(typeof(Customer))]
[JsonSerializable(typeof(CustomerNotFound))]
[JsonSerializable(typeof(Suspended))]
[JsonSerializable(typeof(Unverified))]
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
        options.AddErrorCases<Error>(ShopError.Cases).AddErrorCases<ShopError>(ShopError.Cases);
#if !NET11_0_OR_GREATER
        // .NET 10: CustomerError is a closed hierarchy, read back through its leaves like ShopError.
        options.AddErrorCases<CustomerError>(CustomerError.Cases);
#endif
        return options;
    }

    private sealed class ResultMetadata : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(Result<decimal>) ? JsonMetadataServices.CreateValueInfo<Result<decimal>>(options, new ResultJsonConverter<decimal>())
            : type == typeof(Result<decimal, ShopError>) ? JsonMetadataServices.CreateValueInfo<Result<decimal, ShopError>>(options, new ResultJsonConverter<decimal, ShopError>())
            : type == typeof(Result<Customer, CustomerError>) ? JsonMetadataServices.CreateValueInfo<Result<Customer, CustomerError>>(options, new ResultJsonConverter<Customer, CustomerError>())
#if NET11_0_OR_GREATER
            : type == typeof(CustomerError) ? JsonMetadataServices.CreateValueInfo<CustomerError>(options, new ErrorUnionJsonConverter<CustomerError>(CustomerError.Cases, CustomerError.Wrap))
#endif
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
            .Map<OverLimit>(StatusCodes.Status422UnprocessableEntity)
            .Map<CustomerNotFound>(StatusCodes.Status404NotFound)
            .Map<Suspended>(StatusCodes.Status403Forbidden)
            .Map<Unverified>(StatusCodes.Status403Forbidden));

        var app = builder.Build();
        app.MapGet("/quote/{sku}", (string sku, string qty) => new OpenPricing().Place(sku, qty).ToHttpResult());
        app.MapGet("/closed/quote/{sku}", async (string sku, string qty, CancellationToken ct) =>
            (await new ClosedPricing().PlaceAsync(sku, qty, ct).ConfigureAwait(false)).ToHttpResult());
        // The return type is spelled out: inferred, the request delegate generator emits ResultHttpResult<T, …> with T
        // unbound (on .NET 10 too, through the TError : Error overload).
#if NET11_0_OR_GREATER
        app.MapGet("/customers/{id}", ResultHttpResult<Customer, Error> (string id) => Customers.Find(id).AsResult().ToHttpResult());
#else
        app.MapGet("/customers/{id}", ResultHttpResult<Customer, CustomerError> (string id) => Customers.Find(id).AsResult().ToHttpResult());
#endif
        app.MapPost("/orders", (OrderRequest request) => new ClosedPricing().Check(request).ToHttpResult());
        return app;
    }
}
