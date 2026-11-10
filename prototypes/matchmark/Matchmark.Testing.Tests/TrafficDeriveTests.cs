using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>A spec inferred from recorded traffic (docs/reference/flip.md, step 1, Q14): HAR read, paths templated, schemas inferred.</summary>
[TestFixture]
public sealed class TrafficDeriveTests
{
    [TestCase("/pets/7", "/pets/{petId}")]
    [TestCase("/categories/3f2504e0-4f89-11d3-9a0c-0305e82c3301/items/12", "/categories/{categoryId}/items/{itemId}")]
    [TestCase("/pets", "/pets")]
    public void Ids_in_paths_are_parameters_named_for_their_collection(string path, string template)
        => TrafficDerive.Template(path).Template.Should().Be(template);

    [Test]
    public void A_HAR_file_becomes_a_spec_with_templated_paths_and_inferred_schemas()
    {
        static JsonObject Entry(string method, string url, int status, string? sent, string? answer) => new()
        {
            ["request"] = new JsonObject { ["method"] = method, ["url"] = url, ["postData"] = sent is null ? null : new JsonObject { ["mimeType"] = "application/json", ["text"] = sent } },
            ["response"] = new JsonObject { ["status"] = status, ["content"] = new JsonObject { ["mimeType"] = "application/json; charset=utf-8", ["text"] = answer } },
        };

        var har = new JsonObject { ["log"] = new JsonObject { ["entries"] = new JsonArray(
            Entry("POST", "https://api.example.com/orders", 201, """{"item":"tea","quantity":2}""", """{"id":1,"item":"tea","quantity":2,"placedAt":"2026-11-10T09:00:00Z"}"""),
            Entry("POST", "https://api.example.com/orders", 201, """{"item":"cake","quantity":1,"note":"no nuts"}""", """{"id":2,"item":"cake","quantity":1,"placedAt":"2026-11-10T09:05:00Z","note":"no nuts"}"""),
            Entry("GET", "https://api.example.com/orders/1?expand=true", 200, null, """{"id":1,"item":"tea","quantity":2,"placedAt":"2026-11-10T09:00:00Z"}"""),
            Entry("GET", "https://api.example.com/orders/abc", 400, null, """{"title":"Bad id"}"""),
            Entry("GET", "https://api.example.com/orders/99", 404, null, null)) } };

        var (spec, review) = TrafficDerive.Derive(TrafficDerive.Har(har.ToJsonString()), "Orders");

        spec["paths"]!.AsObject().Select(p => p.Key).Should().Equal("/orders", "/orders/{orderId}");
        var body = spec["paths"]!["/orders"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        body["required"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Equal("item", "quantity");
        body["properties"]!["quantity"]!["type"]!.GetValue<string>().Should().Be("integer");
        body["properties"]!["note"]!["type"]!.AsArray().Select(t => t!.GetValue<string>()).Should().Equal("string", "null");
        var read = spec["paths"]!["/orders/{orderId}"]!["get"]!;
        read["responses"]!.AsObject().Select(r => r.Key).Should().Equal("200", "400", "404");
        read["parameters"]!.AsArray().Should().Contain(p => p!["name"]!.GetValue<string>() == "orderId" && p["schema"]!["type"]!.GetValue<string>() == "integer",
            "the refused /orders/abc shows what isn't an id");
        spec["paths"]!["/orders"]!["post"]!["responses"]!["201"]!["content"]!["application/json"]!["schema"]!["properties"]!["placedAt"]!["format"]!.GetValue<string>().Should().Be("date-time");
        review.Should().Contain(r => r.StartsWith("GET /orders/{orderId}: 404 was seen once", StringComparison.Ordinal));
    }
}
