// The wire format (12b, DECISIONS-TAKEN accepted): success {"ok":true,"value":…}; failure {"ok":false,"error":{type,
// message, context, cause?, …leaf properties}}; Validation failure "errors":[…]; Unit is {}; the zero value throws on write.
// spec: uses Resultful.Text.Json
using System.Text.Json;
using System.Text.Json.Nodes;
using Resultful;
using Resultful.Text.Json;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record Order(int Id);

public static class Cases
{
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful().AddErrorCases<Error>(new[] { typeof(OrderNotFound), typeof(MessageError) });

    static void Same(string expected, string actual) => Check.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"expected {expected}, got {actual}");

    [SpecCase]
    public static void Each_shape_writes_as_pinned()
    {
        Same("""{"ok":true,"value":{"id":1}}""", JsonSerializer.Serialize(Result.Ok(new Order(1)), Options));
        Same("""{"ok":false,"error":{"type":"OrderNotFound","message":"order 2 was not found","context":["loading"],"cause":{"type":"MessageError","message":"db","context":[]},"id":2}}""",
            JsonSerializer.Serialize(Result.Fail<Order>(new OrderNotFound(2) { Cause = Error.From("db") }.WithContext("loading")), Options));
        Same("""{"ok":false,"errors":[{"type":"MessageError","message":"a","context":[]},{"type":"MessageError","message":"b","context":[]}]}""",
            JsonSerializer.Serialize(Validation.Fail<int>(new[] { Error.From("a"), Error.From("b") }), Options));
        Same("""{"ok":true,"value":{}}""", JsonSerializer.Serialize(Result.Ok(), Options));
        Check.Throws<System.InvalidOperationException>(() => JsonSerializer.Serialize(default(Result<int>), Options), "the zero value");
    }

    [SpecCase]
    public static void It_reads_back_equal()
    {
        var r = Result.Fail<Order>(new OrderNotFound(2).WithContext("loading"));
        Check.Equal(r, JsonSerializer.Deserialize<Result<Order>>(JsonSerializer.Serialize(r, Options), Options), "round trip");
    }
}
