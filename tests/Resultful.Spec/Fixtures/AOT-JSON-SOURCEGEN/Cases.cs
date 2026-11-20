// 12b (AOT-safe, source-generation friendly): the converters work with a source-generated JsonSerializerContext and no
// reflection-based metadata. The context lists the value type; the result's metadata comes from the converter.
// (`[JsonSerializable(typeof(Result<Order>))]` doesn't compile: the generator calls the parameterless constructor that
// decision 10 makes an error, CS0619. 22 publishes the Shop native with this pattern.)
// spec: uses Resultful.Text.Json stj-generator
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Resultful;
using Resultful.Text.Json;
using Resultful.Spec.Support;

public sealed record Order(int Id);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Order))]
public partial class ShopJson : JsonSerializerContext;

public static class Cases
{
    [SpecCase]
    public static void A_source_generated_context_writes_and_reads_a_result()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = ShopJson.Default };
        var info = JsonMetadataServices.CreateValueInfo<Result<Order>>(options, new ResultJsonConverter<Order>());
        var json = JsonSerializer.Serialize(Result.Ok(new Order(1)), info);
        Check.Equal("""{"ok":true,"value":{"id":1}}""", json, "success");
        Check.Equal(1, JsonSerializer.Deserialize(json, info).Value.Id, "read back");
    }
}
