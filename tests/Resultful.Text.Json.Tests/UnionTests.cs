#if NET11_0_OR_GREATER
using System.Text.Json;
using CsCheck;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Resultful.CodeAnalysis;
using Resultful.Testing.Json;

namespace Resultful.Text.Json.Tests;

// Decision 17: an error union writes as its leaf and reads back through its cases. Mirrors UERR-JSON-FLAT, -NESTED and
// -UNKNOWN, with the union's members written by hand as Resultful.Generators will write them.
public union OrderError(OrderNotFound, OrderLocked) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new InvalidOperationException("<default OrderError>") };

    public static IReadOnlyList<Type> Cases { get; } = [typeof(OrderNotFound), typeof(OrderLocked)];

    public static OrderError Wrap(Error leaf) => leaf switch
    {
        OrderNotFound nf => nf,
        OrderLocked l => l,
        _ => throw new JsonException($"{leaf.GetType().Name} isn't an OrderError case."),
    };
}

public union CardError(CardDeclined, CardExpired) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new InvalidOperationException("<default CardError>") };
}

public union CheckoutError(OrderNotFound, CardError) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, IErrorUnion u => u.Error, _ => throw new InvalidOperationException("<default CheckoutError>") };

    public static IReadOnlyList<Type> Cases { get; } = [typeof(OrderNotFound), typeof(CardDeclined), typeof(CardExpired)];

    public static CheckoutError Wrap(Error leaf) => leaf switch
    {
        OrderNotFound nf => nf,
        CardDeclined d => (CardError)d,
        CardExpired e => (CardError)e,
        _ => throw new JsonException($"{leaf.GetType().Name} isn't a CheckoutError case."),
    };
}

// A union whose converter comes from an attribute, not the options.
[JsonConverter(typeof(LockConverter))]
public union LockError(OrderLocked) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new InvalidOperationException("<default LockError>") };
}

public sealed class LockConverter : JsonConverter<LockError>
{
    private readonly ErrorUnionJsonConverter<LockError> _inner = new([typeof(OrderLocked)], e => (OrderLocked)e);

    public override LockError Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => _inner.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, LockError value, JsonSerializerOptions options) => _inner.Write(writer, value, options);
}

public class UnionTests
{
    private static JsonSerializerOptions Options() =>
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new ErrorUnionJsonConverter<OrderError>(OrderError.Cases, OrderError.Wrap),
                new ErrorUnionJsonConverter<CheckoutError>(CheckoutError.Cases, CheckoutError.Wrap),
            },
        }.AddResultful().AddErrorCases<Twin.OrderError>([typeof(Twin.OrderLocked)]);

    private const string Locked = """{"ok":false,"error":{"type":"OrderLocked","message":"order 7 is locked by ann","context":[],"id":7,"by":"ann"}}""";

    [Fact]
    public void A_union_failure_round_trips_as_its_leaf()
    {
        Result<Order, OrderError> r = (OrderError)new OrderLocked(7, "ann");
        var json = JsonSerializer.Serialize(r, Options());
        Assert.Equal(Locked, json);
        var back = JsonSerializer.Deserialize<Result<Order, OrderError>>(json, Options());
        Assert.True(back.TryGetError(out var e) && e.Error is OrderLocked { Id: 7, By: "ann" });
    }

    [Fact]
    public void The_closed_hierarchy_twin_writes_the_same_json()
    {
        Result<Order, Twin.OrderError> twin = new Twin.OrderLocked(7, "ann");
        Assert.Equal(Locked, JsonSerializer.Serialize(twin, Options()));
    }

    [Fact]
    public void Writing_a_union_needs_no_registration() =>
        JsonAssert.Equal(Locked, (Result<Order, OrderError>)(OrderError)new OrderLocked(7, "ann"));

    [Fact]
    public void A_nested_union_writes_its_innermost_leaf_and_reads_back_into_the_same_nesting()
    {
        Result<Order, CheckoutError> r = (CheckoutError)(CardError)new CardDeclined("expired");
        var json = JsonSerializer.Serialize(r, Options());
        Assert.Equal("CardDeclined", JsonNode.Parse(json)!["error"]!["type"]!.GetValue<string>());
        var back = JsonSerializer.Deserialize<Result<Order, CheckoutError>>(json, Options());
        Assert.True(back.TryGetError(out var e) && e.Value is CardError c && c.Value is CardDeclined { Reason: "expired" });
    }

    [Fact]
    public void A_union_validation_round_trips()
    {
        var v = Validation.Fail<int, OrderError>([(OrderError)new OrderNotFound(1), (OrderError)new OrderLocked(2, "b")]);
        var json = JsonSerializer.Serialize(v, Options());
        Assert.Equal(v.Errors.Select(e => e.Error), JsonSerializer.Deserialize<Validation<int, OrderError>>(json, Options()).Errors.Select(e => e.Error));
    }

    [Fact]
    public void An_unknown_type_throws_for_a_union_and_for_a_hierarchy()
    {
        const string unknown = """{"ok":false,"error":{"type":"OrderVanished","message":"gone","context":[]}}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<Order, OrderError>>(unknown, Options()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<Order, Twin.OrderError>>(unknown, Options()));
        // A leaf of another union is unknown to this one.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<Order, OrderError>>("""{"ok":false,"error":{"type":"CardDeclined","message":"m","context":[],"reason":"r"}}""", Options()));
    }

    [Fact]
    public void A_union_with_a_JsonConverter_attribute_reads_without_registration()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful();
        Result<Order, LockError> r = (LockError)new OrderLocked(7, "ann");
        var back = JsonSerializer.Deserialize<Result<Order, LockError>>(JsonSerializer.Serialize(r, options), options);
        Assert.True(back.TryGetError(out var e) && e.Error is OrderLocked { Id: 7 });
    }

    [Fact]
    public void Reading_a_union_without_its_converter_says_what_to_add()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful();
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<Order, OrderError>>(Locked, options));
        Assert.Contains("ErrorUnionJsonConverter<OrderError>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_zero_union_throws_on_write_and_cant_be_a_failure()
    {
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(default(OrderError), Options()));
        Assert.Throws<ArgumentException>(() => Result.Fail<int, OrderError>(default(OrderError)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("tr-TR")]
    public void Flat_and_nested_unions_round_trip(string culture) => Wire.InCulture(culture, () =>
    {
        var options = Options();
        var text = Gen.String[Gen.Char[' ', 'ɏ'], 0, 12];
        var flat = Gen.Frequency(
            (1, Gen.Int.Select(id => (OrderError)new OrderNotFound(id))),
            (1, Gen.Select(Gen.Int, text, (id, by) => (OrderError)new OrderLocked(id, by))));
        var nested = Gen.Frequency(
            (1, Gen.Int.Select(id => (CheckoutError)new OrderNotFound(id))),
            (1, text.Select(reason => (CheckoutError)(CardError)new CardDeclined(reason))),
            (1, Gen.Date.Select(d => (CheckoutError)(CardError)new CardExpired(DateOnly.FromDateTime(d)))));
        Gen.Bool.SelectMany(ok => ok ? Gen.Int.Select(Result.Ok<int, OrderError>) : flat.Select(Result.Fail<int, OrderError>)).Sample(r =>
        {
            var json = JsonSerializer.Serialize(r, options);
            var back = JsonSerializer.Deserialize<Result<int, OrderError>>(json, options);
            Assert.Equal(r.IsSuccess, back.IsSuccess);
            Assert.Equal(r.IsSuccess ? null : r.Error.Error, back.IsSuccess ? null : back.Error.Error);
            Assert.Equal(json, JsonSerializer.Serialize(back, options));
        }, iter: 300);
        nested.Array[1, 3].Select(e => Validation.Fail<int, CheckoutError>(e)).Sample(v =>
        {
            var json = JsonSerializer.Serialize(v, options);
            var back = JsonSerializer.Deserialize<Validation<int, CheckoutError>>(json, options);
            Assert.Equal(v.Errors.Select(e => e.Value.GetType()), back.Errors.Select(e => e.Value.GetType()));
            Assert.Equal(v.Errors.Select(e => e.Error), back.Errors.Select(e => e.Error));
            Assert.Equal(json, JsonSerializer.Serialize(back, options));
        }, iter: 300);
    });
}
#endif
