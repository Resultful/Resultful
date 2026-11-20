using System.Text.Json;
using Resultful.Testing.Json;

namespace Resultful.Text.Json.Tests;

// The wire format (DECISIONS-TAKEN 04c, accepted), pinned with JsonAssert. Mirrors JSON-WIRE and CULTURE-JSON-TR.
public class WireFormatTests
{
    [Fact]
    public void A_success_is_ok_true_and_the_value() =>
        JsonAssert.Equal("""{"ok":true,"value":{"id":1,"name":"tea"}}""", Result.Ok(new Order(1, "tea")));

    [Fact]
    public void A_failure_is_ok_false_and_the_error_with_its_context_cause_and_leaf_properties() =>
        JsonAssert.Equal(
            """
            {"ok":false,"error":{
              "type":"OrderNotFound","message":"order 2 was not found","context":["loading"],
              "cause":{"type":"MessageError","message":"db","context":[]},
              "id":2}}
            """,
            Result.Fail<Order>(new OrderNotFound(2) { Cause = Error.From("db") }.WithContext("loading")));

    [Fact]
    public void Members_are_written_in_the_fixed_order()
    {
        var json = JsonSerializer.Serialize(Result.Fail<Order>(new OrderLocked(7, "ann") { Cause = Error.From("db") }.WithContext("a")), Wire.Web());
        Assert.Equal("""{"ok":false,"error":{"type":"OrderLocked","message":"order 7 is locked by ann","context":["a"],"cause":{"type":"MessageError","message":"db","context":[]},"id":7,"by":"ann"}}""", json);
    }

    [Fact]
    public void Context_is_always_written_and_cause_only_when_set() =>
        JsonAssert.Equal("""{"ok":false,"error":{"type":"MessageError","message":"m","context":[]}}""", Result.Fail<int>("m"));

    [Fact]
    public void A_validation_failure_has_errors() =>
        JsonAssert.Equal(
            """{"ok":false,"errors":[{"type":"MessageError","message":"a","context":[]},{"type":"InvalidItem","message":"invalid item","context":[],"price":1.5}]}""",
            Validation.Fail<int>([Error.From("a"), new InvalidItem(1.5m)]));

    [Fact]
    public void A_validation_success_is_ok_true_and_the_value() =>
        JsonAssert.Equal("""{"ok":true,"value":3}""", Validation.Ok<int, PaymentError>(3));

    [Fact]
    public void Unit_is_an_empty_object() =>
        JsonAssert.Equal("""{"ok":true,"value":{}}""", Result.Ok());

    [Fact]
    public void A_hierarchy_error_is_written_as_its_leaf() =>
        JsonAssert.Equal("""{"ok":false,"error":{"type":"CardDeclined","message":"card declined: stolen","context":[],"reason":"stolen"}}""",
            Result.Fail<int, PaymentError>(new CardDeclined("stolen")));

    [Fact]
    public void An_error_that_isnt_an_Error_goes_through_the_options() =>
        JsonAssert.Equal("""{"ok":false,"error":"nope"}""", Result.Fail<int, string>("nope"));

    [Fact]
    public void A_ValidationFailed_carried_through_a_result_writes_its_errors() =>
        JsonAssert.Equal(
            """{"ok":false,"error":{"type":"ValidationFailed","message":"2 validation error(s)","context":[],"errors":[{"type":"MessageError","message":"a","context":[]},{"type":"OrderNotFound","message":"order 1 was not found","context":[],"id":1}]}}""",
            Result.Fail<int>(new ValidationFailed<Error>([Error.From("a"), new OrderNotFound(1)])));

    [Fact]
    public void Without_a_naming_policy_leaf_properties_keep_their_names_and_wire_keys_stay_lowercase() =>
        JsonAssert.Equal("""{"ok":false,"error":{"type":"OrderLocked","message":"order 1 is locked by b","context":[],"Id":1,"By":"b"}}""",
            Result.Fail<int>(new OrderLocked(1, "b")), Wire.Plain());

    [Fact]
    public void An_Error_on_its_own_is_written_as_the_error_object() =>
        JsonAssert.Equal("""{"type":"OrderNotFound","message":"order 3 was not found","context":["x"],"id":3}""", (Error)new OrderNotFound(3).WithContext("x"));

    [Fact]
    public void The_zero_value_throws_on_write()
    {
        var options = Wire.Web();
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(default(Result<int>), options));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(default(Result<int, PaymentError>), options));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(default(Validation<int>), options));
        var ex = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(default(Validation<int, PaymentError>), options));
        Assert.Contains("zero value", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tr-TR")]
    public void Json_doesnt_depend_on_the_culture(string culture) => Wire.InCulture(culture, () =>
        JsonAssert.Equal("""{"ok":false,"error":{"type":"InvalidItem","message":"invalid item","context":["İi"],"price":1.5}}""",
            Result.Fail<int>(new InvalidItem(1.5m).WithContext("İi"))));

    [Fact]
    public void A_leaf_property_that_collides_with_a_wire_key_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(Result.Fail<int>(new Clash("x")), Wire.Web()));
        Assert.Contains("\"type\"", ex.Message, StringComparison.Ordinal);
    }

    public sealed record Clash(string Type) : Error("clash");
}
