// Decision 17 (12b): an error union writes as its leaf and reads back through its cases. The twin writes the same JSON.
// spec: tfm net11.0
// spec: uses Resultful.Text.Json
using System.Text.Json;
using System.Text.Json.Nodes;
using Resultful;
using Resultful.Text.Json;
using Resultful.Spec.Support;
using Resultful.CodeAnalysis;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");
public sealed record Order(int Id);
public union OrderError(OrderNotFound, OrderLocked) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new System.InvalidOperationException() };
}

namespace Twin
{
    [ClosedHierarchy]
    public abstract record OrderError : Error { private protected OrderError(string m) : base(m) { } }
    public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
}

public static class Cases
{
    static readonly System.Type[] Leaves = { typeof(OrderNotFound), typeof(OrderLocked) };

    static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful()
            .AddErrorCases<OrderError>(Leaves).AddErrorCases<Twin.OrderError>(new[] { typeof(Twin.OrderLocked) });
        options.Converters.Add(new ErrorUnionJsonConverter<OrderError>(Leaves, e => e switch
        {
            OrderNotFound nf => nf,
            OrderLocked l => l,
            _ => throw new JsonException("not an OrderError case"),
        }));
        return options;
    }

    const string Expected = """{"ok":false,"error":{"type":"OrderLocked","message":"order 7 is locked by ann","context":[],"id":7,"by":"ann"}}""";

    [SpecCase]
    public static void A_union_failure_round_trips_as_its_leaf()
    {
        Result<Order, OrderError> r = (OrderError)new OrderLocked(7, "ann");
        var json = JsonSerializer.Serialize(r, Options());
        Check.True(JsonNode.DeepEquals(JsonNode.Parse(Expected), JsonNode.Parse(json)), json);
        Check.True(json.StartsWith("""{"ok":false,"error":{"type":"OrderLocked","message":""", System.StringComparison.Ordinal), "property order: " + json);
        var back = JsonSerializer.Deserialize<Result<Order, OrderError>>(json, Options());
        Check.True(back.TryGetError(out var e) && e.Error is OrderLocked { Id: 7, By: "ann" }, "read back");
    }

    [SpecCase]
    public static void The_twin_writes_the_same_json()
    {
        Result<Order, Twin.OrderError> twin = new Twin.OrderLocked(7, "ann");
        Check.Equal(JsonSerializer.Serialize((Result<Order, OrderError>)(OrderError)new OrderLocked(7, "ann"), Options()), JsonSerializer.Serialize(twin, Options()), "same JSON");
    }
}
