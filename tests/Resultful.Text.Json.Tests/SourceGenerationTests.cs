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

    [Fact]
    public void A_leaf_missing_from_the_context_fails_loudly_instead_of_reflecting()
    {
        var info = JsonMetadataServices.CreateValueInfo<Result<Order>>(Options(), new ResultJsonConverter<Order>());
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(Result.Fail<Order>(new InvalidItem(1m)), info));
    }
}
