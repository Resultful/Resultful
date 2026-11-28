using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json.Tests;

// The JSON read rules (round 4): "type" is read back through a registered case list; an unknown "type" throws.
public class ReadTests
{
    private static T Read<T>(string json, JsonSerializerOptions? options = null) => JsonSerializer.Deserialize<T>(json, options ?? Wire.Web())!;

    [Fact]
    public void A_failure_reads_back_equal()
    {
        var r = Result.Fail<Order>(new OrderNotFound(2) { Cause = new OrderLocked(2, "ann").WithContext("db") }.WithContext("loading"));
        Assert.Equal(r, Read<Result<Order>>(JsonSerializer.Serialize(r, Wire.Web())));
    }

    [Fact]
    public void A_hierarchy_error_reads_back_as_its_leaf()
    {
        var back = Read<Result<int, PaymentError>>("""{"ok":false,"error":{"type":"CardDeclined","message":"card declined: stolen","context":[],"reason":"stolen"}}""");
        Assert.Equal(new CardDeclined("stolen"), back.Error);
    }

    [Fact]
    public void Members_may_come_in_any_order()
    {
        Assert.Equal(Result.Ok(5), Read<Result<int>>("""{"value":5,"ok":true}"""));
        Assert.Equal(Result.Fail<int>(new OrderNotFound(1)), Read<Result<int>>("""{"error":{"id":1,"context":[],"message":"m","type":"OrderNotFound"},"ok":false}"""));
    }

    [Theory]
    [InlineData("""{"ok":false,"error":{"type":"OrderVanished","message":"gone","context":[]}}""")]
    [InlineData("""{"ok":false,"error":{"message":"no type","context":[]}}""")]
    [InlineData("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","context":"not an array","id":1}}""")]
    [InlineData("""{"ok":false,"error":{"type":"ValidationFailed","message":"m","context":[],"errors":[{"type":"MessageError","message":"a","context":[]}]}}""")]
    public void An_unknown_or_missing_type_throws_JsonException(string json)
    {
        Assert.Throws<JsonException>(() => Read<Result<int>>(json));
        Assert.Throws<JsonException>(() => Read<Result<int, PaymentError>>(json));
    }

    [Fact]
    public void A_leaf_outside_the_hierarchy_is_unknown_to_it()
    {
        var ex = Assert.Throws<JsonException>(() => Read<Result<int, PaymentError>>("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","context":[],"id":1}}"""));
        Assert.Contains("AddErrorCases<PaymentError>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unregistered_leaf_is_unknown()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful();
        Assert.Throws<JsonException>(() => Read<Result<int>>("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","context":[],"id":1}}""", options));
        // The core's own leaves need no registration.
        Assert.Equal(Result.Fail<int>("m"), Read<Result<int>>("""{"ok":false,"error":{"type":"MessageError","message":"m","context":[]}}""", options));
    }

    [Fact]
    public void Cases_from_the_core_registry_are_read_back()
    {
        // Proposed decision 25: one ErrorCases, filled once, serves every format.
        var cases = new ErrorCases().Add<Error>([typeof(OrderNotFound)]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful().AddErrorCases(cases);
        var r = Result.Fail<int>(new OrderNotFound(1));
        Assert.Equal(r, Read<Result<int>>(JsonSerializer.Serialize(r, options), options));
    }

    [Theory]
    [InlineData("""{"value":1}""")]
    [InlineData("""{"ok":true}""")]
    [InlineData("""{"ok":true,"value":1,"error":{"type":"MessageError","message":"m","context":[]}}""")]
    [InlineData("""{"ok":false}""")]
    [InlineData("""{"ok":false,"value":1,"error":{"type":"MessageError","message":"m","context":[]}}""")]
    [InlineData("""{"ok":"yes","value":1}""")]
    [InlineData("""null""")]
    [InlineData("""[1]""")]
    public void A_malformed_envelope_throws_JsonException(string json) => Assert.Throws<JsonException>(() => Read<Result<int>>(json));

    [Fact]
    public void An_envelope_error_names_the_type_once()
    {
        var ex = Assert.Throws<JsonException>(() => Read<Result<Unit>>("""{"ok":false}"""));
        Assert.Equal("A failed Result<Unit> has no \"error\".", ex.Message);
        Assert.Contains("Validation<Int32,PaymentError>", Assert.Throws<JsonException>(() => Read<Validation<int, PaymentError>>("""{"ok":false,"errors":[]}""")).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"ok":false,"errors":[]}""")]
    [InlineData("""{"ok":false,"errors":{}}""")]
    [InlineData("""{"ok":false,"error":{"type":"MessageError","message":"m","context":[]}}""")]
    public void A_validation_failure_needs_a_non_empty_errors_array(string json) => Assert.Throws<JsonException>(() => Read<Validation<int>>(json));

    [Fact]
    public void Unit_reads_from_an_empty_object_or_a_missing_value()
    {
        Assert.Equal(Result.Ok(), Read<Result<Unit>>("""{"ok":true,"value":{}}"""));
        Assert.Equal(Result.Ok(), Read<Result<Unit>>("""{"ok":true}"""));
        Assert.Throws<JsonException>(() => Read<Result<Unit>>("""{"ok":true,"value":{"x":1}}"""));
    }

    [Fact]
    public void Unknown_members_are_skipped_unless_the_options_disallow_them()
    {
        Assert.Equal(Result.Ok(1), Read<Result<int>>("""{"ok":true,"value":1,"extra":[1,2]}"""));
        var strict = Wire.Web();
        strict.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        Assert.Throws<JsonException>(() => Read<Result<int>>("""{"ok":true,"value":1,"extra":[1,2]}""", strict));
    }

    [Fact]
    public void A_ValidationFailed_reads_back_with_its_errors()
    {
        var r = Result.Fail<int>(new ValidationFailed<Error>([Error.From("a"), new OrderNotFound(1)]).WithContext("form"));
        Assert.Equal(r, Read<Result<int>>(JsonSerializer.Serialize(r, Wire.Web())));
    }

    // The rule: ValidationFailed is written as ValidationFailed<X>, X being its closed type argument's wire name, and is
    // read back as that closed type. So Validation<T, X>.ToResult() round-trips for X = Error, a hierarchy or a leaf.
    [Theory]
    [InlineData("web")]
    [InlineData("plain")]
    public void Validation_ToResult_round_trips_for_every_element_type(string naming)
    {
        var options = naming == "web" ? Wire.Web() : Wire.Plain();
        var ofError = Validation.Fail<int>([Error.From("a"), new CardDeclined("b")]).ToResult();
        var ofHierarchy = Validation.Fail<int, PaymentError>([new CardDeclined("x"), new CardExpired(new DateOnly(2026, 2, 1))]).ToResult();
        var ofLeaf = Validation.Fail<int, OrderNotFound>([new OrderNotFound(1), new OrderNotFound(2)]).ToResult();
        foreach (var r in new[] { ofError, ofHierarchy, ofLeaf })
        {
            var back = Read<Result<int>>(JsonSerializer.Serialize(r, options), options);
            Assert.Equal(r, back);
            Assert.Equal(r.Error.GetType(), back.Error.GetType());
        }

        Assert.IsType<ValidationFailed<PaymentError>>(Read<Result<int>>(JsonSerializer.Serialize(ofHierarchy, options), options).Error);
    }

    [Fact]
    public void Registering_a_closed_ValidationFailed_as_a_case_is_harmless()
    {
        var options = Wire.Web().AddErrorCases<Error>([typeof(ValidationFailed<PaymentError>)]);
        var r = Validation.Fail<int, PaymentError>([new CardDeclined("x")]).ToResult();
        Assert.Equal(r, Read<Result<int>>(JsonSerializer.Serialize(r, options), options));
        var plain = Validation.Fail<int>([Error.From("a")]).ToResult();
        Assert.Equal(plain, Read<Result<int>>(JsonSerializer.Serialize(plain, options), options));
    }

    [Fact]
    public void A_ValidationFailed_whose_element_type_isnt_registered_throws()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful();
        var json = JsonSerializer.Serialize(Validation.Fail<int, PaymentError>([new CardDeclined("x")]).ToResult(), options);
        var ex = Assert.Throws<JsonException>(() => Read<Result<int>>(json, options));
        Assert.Contains("AddErrorCases<PaymentError>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ValidationFailed_element_outside_its_type_throws() =>
        Assert.Throws<JsonException>(() => Read<Result<int>>("""{"ok":false,"error":{"type":"ValidationFailed<PaymentError>","message":"m","context":[],"errors":[{"type":"OrderNotFound","message":"m","context":[],"id":1}]}}"""));

    [Fact]
    public void A_member_typed_as_a_hierarchy_base_reads_back()
    {
        var r = Result.Fail<int>(new PaymentFailed(3, new CardExpired(new DateOnly(2026, 3, 1)) { Cause = Error.From("bank") }));
        Assert.Equal(r, Read<Result<int>>(JsonSerializer.Serialize(r, Wire.Web())));
        Assert.Equal(r, Read<Result<int>>(JsonSerializer.Serialize(r, Wire.Plain()), Wire.Plain()));
    }

    [Theory]
    [InlineData("""{"ok":true,"ok":false,"error":{"type":"MessageError","message":"m","context":[]}}""")]
    [InlineData("""{"ok":true,"value":1,"value":2}""")]
    [InlineData("""{"ok":false,"error":{"type":"MessageError","message":"m","context":[]},"error":{"type":"MessageError","message":"n","context":[]}}""")]
    public void A_repeated_envelope_member_throws_JsonException(string json) => Assert.Throws<JsonException>(() => Read<Result<int>>(json));

    [Fact]
    public void A_repeated_errors_member_throws_JsonException() =>
        Assert.Throws<JsonException>(() => Read<Validation<int>>("""{"ok":false,"errors":[{"type":"MessageError","message":"m","context":[]}],"errors":[{"type":"MessageError","message":"n","context":[]}]}"""));

    [Theory]
    [InlineData("""{"ok":false,"error":{"type":"MessageError","type":"OrderNotFound","message":"m","context":[]}}""")]
    [InlineData("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","context":[],"id":1,"id":2}}""")]
    [InlineData("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","message":"n","context":[],"id":1}}""")]
    public void A_repeated_error_member_throws_JsonException_not_ArgumentException(string json)
    {
        var ex = Record.Exception(() => Read<Result<int>>(json));
        Assert.IsType<JsonException>(ex);
    }

    [Theory]
    [InlineData("""{"ok":false,"error":{"type":"MessageError","context":[]}}""")]
    [InlineData("""{"ok":false,"error":{"type":"MessageError","message":null,"context":[]}}""")]
    [InlineData("""{"ok":false,"error":{"type":"MessageError","message":3,"context":[]}}""")]
    [InlineData("""{"ok":false,"error":{"type":"OrderNotFound","context":[],"id":1}}""")]
    [InlineData("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","context":null,"id":1}}""")]
    [InlineData("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","context":[],"cause":"db","id":1}}""")]
    public void A_missing_or_mistyped_wire_member_throws_JsonException(string json) => Assert.Throws<JsonException>(() => Read<Result<int>>(json));

    [Fact]
    public void Cases_cant_be_added_once_the_options_are_in_use()
    {
        var options = Wire.Web();
        JsonSerializer.Serialize(Result.Ok(1), options);
        Assert.Throws<InvalidOperationException>(() => options.AddErrorCases<Error>([typeof(Other.OrderNotFound)]));
    }

    [Fact]
    public void An_explicit_ErrorJsonConverter_supplies_the_cases()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new ErrorJsonConverter<PaymentError>(PaymentError.Cases) } };
        var back = JsonSerializer.Deserialize<PaymentError>("""{"type":"CardExpired","message":"card expired","context":[],"on":"2026-01-31"}""", options);
        Assert.Equal(new CardExpired(new DateOnly(2026, 1, 31)), back);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaymentError>("""{"type":"CardStolen","message":"m","context":[]}""", options));
    }

    [Fact]
    public void An_explicit_ErrorJsonConverter_wins_over_the_factory_registry()
    {
        // Only CardDeclined is a case here, although the registry (Wire.Web) also knows CardExpired.
        var options = Wire.Web();
        options.Converters.Add(new ErrorJsonConverter<PaymentError>([typeof(CardDeclined)]));
        const string expired = """{"type":"CardExpired","message":"card expired","context":[],"on":"2026-01-31"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaymentError>(expired, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int, PaymentError>>("{\"ok\":false,\"error\":" + expired + "}", options));
        Assert.Equal(new CardExpired(new DateOnly(2026, 1, 31)), JsonSerializer.Deserialize<PaymentError>(expired, Wire.Web()));
    }

    [Fact]
    public void Registration_refuses_cases_that_couldnt_be_read_back()
    {
        var options = new JsonSerializerOptions();
        Assert.Throws<ArgumentException>(() => options.AddErrorCases<PaymentError>([typeof(OrderNotFound)]));
        Assert.Throws<ArgumentException>(() => options.AddErrorCases<Error>([typeof(PaymentError)]));
        Assert.Throws<ArgumentException>(() => options.AddErrorCases<Error>([typeof(string)]));
        var collision = Assert.Throws<ArgumentException>(() => options.AddErrorCases<Error>([typeof(OrderNotFound), typeof(Other.OrderNotFound)]));
        Assert.Contains("RFG0008", collision.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new ErrorJsonConverter<PaymentError>([typeof(OrderLocked)]));
    }

    [Fact]
    public void The_cases_survive_a_copy_of_the_options()
    {
        var copy = new JsonSerializerOptions(Wire.Web());
        Assert.Equal(Result.Fail<int>(new OrderNotFound(4)), Read<Result<int>>("""{"ok":false,"error":{"type":"OrderNotFound","message":"m","context":[],"id":4}}""", copy));
    }

    [Fact]
    public void Property_names_can_be_read_case_insensitively()
    {
        var options = Wire.Web();
        options.PropertyNameCaseInsensitive = true;
        Assert.Equal(Result.Fail<int>(new OrderLocked(1, "x")), Read<Result<int>>("""{"OK":false,"Error":{"type":"OrderLocked","message":"m","context":[],"ID":1,"BY":"x"}}""", options));
    }
}
