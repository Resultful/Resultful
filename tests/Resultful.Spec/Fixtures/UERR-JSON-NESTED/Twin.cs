// spec: tfm net11.0
// spec: uses Resultful.Text.Json
using System.Text.Json;
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Text.Json;
using Resultful.Spec.Support;

namespace Twin
{
    [ClosedHierarchy] public abstract record OrderError : Error { private protected OrderError(string m) : base(m) { } }
    [ClosedHierarchy] public abstract record PaymentError : OrderError { private protected PaymentError(string m) : base(m) { } }
    public sealed record CardDeclined(string Reason) : PaymentError($"card declined: {Reason}");
}

public static class TwinCases
{
    [SpecCase]
    public static void The_twin_writes_the_same_json()
    {
        System.Type[] leaves = { typeof(OrderNotFound), typeof(CardDeclined) };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful()
            .AddErrorCases<OrderError>(leaves).AddErrorCases<Twin.OrderError>(new[] { typeof(Twin.CardDeclined) });
        options.Converters.Add(new ErrorUnionJsonConverter<OrderError>(leaves, e => e switch
        {
            OrderNotFound nf => nf,
            CardDeclined c => (PaymentError)c,
            _ => throw new JsonException("not a case"),
        }));
        Result<Order, OrderError> union = (OrderError)(PaymentError)new CardDeclined("expired");
        Result<Order, Twin.OrderError> twin = new Twin.CardDeclined("expired");
        Check.Equal(JsonSerializer.Serialize(twin, options), JsonSerializer.Serialize(union, options), "same JSON");
    }
}
