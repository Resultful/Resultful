// Round 4 (12b): reading an unknown "type" throws JsonException, for a union and for a closed hierarchy.
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

[ClosedHierarchy]
public abstract record PaymentError : Error { private protected PaymentError(string m) : base(m) { } }
public sealed record CardDeclined(string Reason) : PaymentError("declined");

public static class Cases
{
    const string Unknown = """{"ok":false,"error":{"type":"OrderVanished","message":"gone","context":[]}}""";

    [SpecCase]
    public static void An_unknown_type_throws()
    {
        System.Type[] leaves = { typeof(OrderNotFound), typeof(OrderLocked) };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful()
            .AddErrorCases<OrderError>(leaves).AddErrorCases<PaymentError>(new[] { typeof(CardDeclined) });
        options.Converters.Add(new ErrorUnionJsonConverter<OrderError>(leaves, e => e switch { OrderNotFound n => n, OrderLocked l => l, _ => throw new JsonException() }));
        Check.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<Order, OrderError>>(Unknown, options), "union");
        Check.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<Order, PaymentError>>(Unknown, options), "hierarchy");
    }
}
