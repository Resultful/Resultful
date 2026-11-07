using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace Resultful.OpenApi.Sample.Tests;

/// <summary>
/// The regression suite: plain HTTP against the running app, knowing nothing about how it is built. This is the
/// suite that defines "done" for the generated server; it would pass against any implementation of the spec.
/// </summary>
[TestFixture]
public sealed class RegressionSuite
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task ListPets_starts_empty()
    {
        var response = await _client.GetAsync("/pets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).AsArray().Should().BeEmpty();
    }

    [Test]
    public async Task CreatePet_returns_201_with_the_created_pet()
    {
        var response = await Post(Dog("Rex"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var pet = await Json(response);
        pet["id"]!.GetValue<long>().Should().BePositive();
        pet["name"]!.GetValue<string>().Should().Be("Rex");
        pet["status"]!.GetValue<string>().Should().Be("available");
        pet["details"]!["kind"]!.GetValue<string>().Should().Be("dog");
        pet["details"]!["goodBoy"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public async Task CreatePet_with_a_taken_name_returns_409_problem()
    {
        await Post(Dog("Rex"));

        var response = await Post(Dog("rex"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["title"]!.GetValue<string>().Should().Be("Name taken");
    }

    [Test]
    public async Task CreatePet_breaking_schema_constraints_returns_400_validation_problem()
    {
        var response = await Post(new JsonObject
        {
            ["name"] = "",
            ["tag"] = "NOT LOWER",
            ["details"] = new JsonObject { ["kind"] = "cat", ["livesLeft"] = 12 },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var errors = (await Json(response))["errors"]!.AsObject();
        errors.Select(e => e.Key).Should().BeEquivalentTo("name", "tag", "details.livesLeft");
    }

    [Test]
    public async Task CreatePet_missing_a_required_property_returns_400()
    {
        var response = await Post(new JsonObject { ["name"] = "Rex" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreatePet_accepts_the_discriminator_anywhere_in_the_object()
    {
        var response = await Post(new JsonObject
        {
            ["name"] = "Tom",
            ["details"] = new JsonObject { ["livesLeft"] = 9, ["kind"] = "cat" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task GetPet_returns_the_pet_with_its_oneOf_details()
    {
        var id = await CreatedId(Cat("Tom", livesLeft: 7));

        var response = await _client.GetAsync($"/pets/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = (await Json(response))["details"]!;
        details["kind"]!.GetValue<string>().Should().Be("cat");
        details["livesLeft"]!.GetValue<int>().Should().Be(7);
    }

    [Test]
    public async Task GetPet_that_does_not_exist_returns_404_problem()
    {
        var response = await _client.GetAsync("/pets/42");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["detail"]!.GetValue<string>().Should().Be("There is no pet 42.");
    }

    [Test]
    public async Task UpsertPet_creates_with_201_then_replaces_with_200()
    {
        var created = await _client.PutAsJsonAsync("/pets/7", Dog("Rex"));
        var replaced = await _client.PutAsJsonAsync("/pets/7", Dog("Max"));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        replaced.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(replaced))["name"]!.GetValue<string>().Should().Be("Max");
    }

    [Test]
    public async Task DeletePet_returns_204_then_404()
    {
        var id = await CreatedId(Dog("Rex"));

        (await _client.DeleteAsync($"/pets/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/pets/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ListPets_honours_the_limit()
    {
        await Post(Dog("A"));
        await Post(Dog("B"));
        await Post(Dog("C"));

        var response = await _client.GetAsync("/pets?limit=2");

        (await Json(response)).AsArray().Select(p => p!["name"]!.GetValue<string>()).Should().Equal("A", "B");
    }

    private static JsonObject Dog(string name)
        => new() { ["name"] = name, ["details"] = new JsonObject { ["kind"] = "dog", ["goodBoy"] = true } };

    private static JsonObject Cat(string name, int livesLeft)
        => new() { ["name"] = name, ["details"] = new JsonObject { ["kind"] = "cat", ["livesLeft"] = livesLeft } };

    private Task<HttpResponseMessage> Post(JsonObject body) => _client.PostAsJsonAsync("/pets", body);

    private async Task<long> CreatedId(JsonObject body) => (await Json(await Post(body)))["id"]!.GetValue<long>();

    private static async Task<JsonNode> Json(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
}
