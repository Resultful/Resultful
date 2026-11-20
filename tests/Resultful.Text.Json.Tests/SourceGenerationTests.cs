using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Resultful.Text.Json.Tests;

// AOT-JSON-SOURCEGEN: a source-generated context, no reflection-based metadata. The context lists the value type and
// the leaves; the result's metadata comes from the converter, because [JsonSerializable(typeof(Result<Order>))] calls
// the parameterless constructor that decision 10 makes an error (CS0619).
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(OrderNotFound))]
[JsonSerializable(typeof(OrderLocked))]
[JsonSerializable(typeof(CardDeclined))]
[JsonSerializable(typeof(CardExpired))]
public partial class ShopJson : JsonSerializerContext;

public class SourceGenerationTests
{
    private static JsonSerializerOptions Options() =>
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = ShopJson.Default }
            .AddErrorCases<Error>([typeof(OrderNotFound), typeof(OrderLocked)]);

    [Fact]
    public void A_source_generated_context_writes_and_reads_a_success()
    {
        var info = JsonMetadataServices.CreateValueInfo<Result<Order>>(Options(), new ResultJsonConverter<Order>());
        var json = JsonSerializer.Serialize(Result.Ok(new Order(1, "tea")), info);
        Assert.Equal("""{"ok":true,"value":{"id":1,"name":"tea"}}""", json);
        Assert.Equal(1, JsonSerializer.Deserialize(json, info).Value.Id);
    }

    [Fact]
    public void A_source_generated_context_writes_and_reads_a_failure_with_a_cause()
    {
        var info = JsonMetadataServices.CreateValueInfo<Result<Order>>(Options(), new ResultJsonConverter<Order>());
        var r = Result.Fail<Order>(new OrderLocked(7, "ann") { Cause = new OrderNotFound(7) }.WithContext("saving"));
        var json = JsonSerializer.Serialize(r, info);
        Assert.Equal("""{"ok":false,"error":{"type":"OrderLocked","message":"order 7 is locked by ann","context":["saving"],"cause":{"type":"OrderNotFound","message":"order 7 was not found","context":[],"id":7},"id":7,"by":"ann"}}""", json);
        Assert.Equal(r, JsonSerializer.Deserialize(json, info));
    }

    // The pattern for an app (22, and 20a's registration): a resolver that hands out the converter-backed metadata for
    // each result type, combined with the source-generated context, so plain JsonSerializer.Serialize(value, options)
    // works with no reflection and no AddResultful (which needs dynamic code).
    private sealed class ResultResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(Result<Order>) ? JsonMetadataServices.CreateValueInfo<Result<Order>>(options, new ResultJsonConverter<Order>())
            : type == typeof(Validation<Order, PaymentError>) ? JsonMetadataServices.CreateValueInfo<Validation<Order, PaymentError>>(options, new ValidationJsonConverter<Order, PaymentError>())
            : null;
    }

    [Fact]
    public void A_combined_resolver_serves_plain_Serialize_and_Deserialize()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = JsonTypeInfoResolver.Combine(new ResultResolver(), ShopJson.Default) }
            .AddErrorCases<Error>([typeof(OrderNotFound), typeof(OrderLocked)]).AddErrorCases<PaymentError>(PaymentError.Cases);
        var r = Result.Fail<Order>(new OrderLocked(7, "ann") { Cause = new OrderNotFound(7) });
        var json = JsonSerializer.Serialize(r, options);
        Assert.Equal(r, JsonSerializer.Deserialize<Result<Order>>(json, options));
        Assert.Equal("""{"ok":true,"value":{"id":1,"name":"tea"}}""", JsonSerializer.Serialize(Result.Ok(new Order(1, "tea")), options));
        var v = Validation.Fail<Order, PaymentError>([new CardDeclined("x")]);
        Assert.Equal(v, JsonSerializer.Deserialize<Validation<Order, PaymentError>>(JsonSerializer.Serialize(v, options), options));
        // A type the resolvers don't know fails loudly instead of reflecting.
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(Result.Ok(1), options));
    }

    [Fact]
    public void A_leaf_missing_from_the_context_fails_loudly_instead_of_reflecting()
    {
        var info = JsonMetadataServices.CreateValueInfo<Result<Order>>(Options(), new ResultJsonConverter<Order>());
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(Result.Fail<Order>(new InvalidItem(1m)), info));
    }
}
