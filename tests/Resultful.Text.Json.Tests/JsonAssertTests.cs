using System.Text.Json;
using Resultful.Testing.Json;

namespace Resultful.Text.Json.Tests;

// JsonAssert's own tests: it passes when the structure matches, and fails readably when it doesn't.
public class JsonAssertTests
{
    [Fact]
    public void Member_order_and_whitespace_dont_matter() =>
        JsonAssert.Equal(
            """
            {
              "value": { "name": "tea", "id": 1 },
              "ok": true
            }
            """,
            Result.Ok(new Order(1, "tea")));

    [Fact]
    public void A_difference_prints_both_sides_and_where_they_differ()
    {
        var ex = Assert.Throws<JsonAssertException>(() =>
            JsonAssert.Equal("""{"ok":false,"error":{"type":"OrderNotFound","message":"order 2 was not found","context":[],"id":2}}""", Result.Fail<int>(new OrderNotFound(3))));
        Assert.Contains("JSON differs at $.error.message", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Expected:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\"id\": 2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Actual:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\"id\": 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("""Actual, as written: {"ok":false""", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_or_extra_member_is_named()
    {
        var missing = Assert.Throws<JsonAssertException>(() => JsonAssert.EqualJson("""{"a":1,"b":2}""", """{"a":1}"""));
        Assert.Contains("$.b (missing from actual)", missing.Message, StringComparison.Ordinal);
        var extra = Assert.Throws<JsonAssertException>(() => JsonAssert.EqualJson("""{"a":1}""", """{"a":1,"c":3}"""));
        Assert.Contains("$.c (not expected)", extra.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Array_order_and_length_matter()
    {
        var order = Assert.Throws<JsonAssertException>(() => JsonAssert.Equal("""{"ok":false,"errors":[{"type":"MessageError","message":"b","context":[]},{"type":"MessageError","message":"a","context":[]}]}""",
            Validation.Fail<int>([Error.From("a"), Error.From("b")])));
        Assert.Contains("$.errors[0].message", order.Message, StringComparison.Ordinal);
        var length = Assert.Throws<JsonAssertException>(() => JsonAssert.EqualJson("""[1,2]""", """[1,2,3]"""));
        Assert.Contains("expected 2 items, actual 3", length.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_case_is_caught()
    {
        var ex = Assert.Throws<JsonAssertException>(() => JsonAssert.Equal("""{"ok":true,"value":1}""", Result.Fail<int>("m")));
        Assert.Contains("$.ok: expected true, actual false", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_expected_JSON_is_reported_as_such()
    {
        var ex = Assert.Throws<JsonAssertException>(() => JsonAssert.Equal("""{"ok":tru""", Result.Ok(1)));
        Assert.StartsWith("The expected JSON isn't valid", ex.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public void Numbers_compare_by_value() => JsonAssert.EqualJson("""{"price":1.50}""", """{"price":1.5}""");

    [Fact]
    public void The_fixed_options_are_web_defaults_with_Resultful()
    {
        Assert.Same(JsonNamingPolicy.CamelCase, JsonAssert.Options.PropertyNamingPolicy);
        Assert.Contains(JsonAssert.Options.Converters, c => c is ResultfulJsonConverterFactory);
    }
}
