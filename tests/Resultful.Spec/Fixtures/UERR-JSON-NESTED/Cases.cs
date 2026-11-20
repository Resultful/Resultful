// Decision 17 (12b): a nested union writes as its innermost leaf and reads back into the same nesting.
// spec: tfm net11.0
// spec: uses Resultful.Text.Json
using System.Text.Json;
using System.Text.Json.Nodes;
using Resultful;
using Resultful.Text.Json;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record CardDeclined(string Reason) : Error($"card declined: {Reason}");
public sealed record Order(int Id);
public union PaymentError(CardDeclined) : IErrorUnion { public Error Error => (Error)Value!; }
public union OrderError(OrderNotFound, PaymentError) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, IErrorUnion u => u.Error, _ => throw new System.InvalidOperationException() };
}

public static class Cases
{
    [SpecCase]
    public static void The_innermost_leaf_is_written_and_read()
    {
        System.Type[] leaves = { typeof(OrderNotFound), typeof(CardDeclined) };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful().AddErrorCases<OrderError>(leaves);
        options.Converters.Add(new ErrorUnionJsonConverter<OrderError>(leaves, e => e switch
        {
            OrderNotFound nf => nf,
            CardDeclined c => (PaymentError)c,
            _ => throw new JsonException("not a case"),
        }));
        Result<Order, OrderError> r = (OrderError)(PaymentError)new CardDeclined("expired");
        var json = JsonSerializer.Serialize(r, options);
        Check.True(JsonNode.Parse(json)!["error"]!["type"]!.GetValue<string>() == "CardDeclined", json);
        var back = JsonSerializer.Deserialize<Result<Order, OrderError>>(json, options);
        Check.True(back.TryGetError(out var e) && e.Value is PaymentError p && p.Value is CardDeclined { Reason: "expired" }, "same nesting");
    }
}
