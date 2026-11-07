using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The regression suite: plain HTTP against the running app, knowing nothing about how it is built. This is the
/// suite that defines "done" for the generated server; it would pass against any implementation of the spec.
/// Each test starts from a named seed scenario.
/// </summary>
[TestFixture]
public sealed class RegressionSuite
{
    private PetStoreApp _app = null!;
    private HttpClient _client = null!;

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app.Dispose();
    }

    [Test]
    public async Task ListPets_on_an_empty_store_is_empty()
    {
        Start("Empty");

        var response = await _client.GetAsync("/pets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).AsArray().Should().BeEmpty();
    }

    [Test]
    public async Task ListPets_returns_the_seeded_pets_in_id_order()
    {
        Start("Demo");

        var pets = (await Json(await _client.GetAsync("/pets"))).AsArray();

        Names(pets).Should().Equal("Rex", "Tom", "Bella");
        pets[2]!["status"]!.GetValue<string>().Should().Be("adopted");
    }

    [Test]
    public async Task ListPets_returns_100_by_default_and_honours_the_limit()
    {
        Start("Crowded");

        (await Json(await _client.GetAsync("/pets"))).AsArray().Should().HaveCount(100);
        Names((await Json(await _client.GetAsync("/pets?limit=2"))).AsArray()).Should().Equal("Cat 001", "Dog 002");
    }

    [Test]
    public async Task CreatePet_returns_201_with_the_created_pet()
    {
        Start("Empty");

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
    public async Task CreatePet_with_a_taken_name_returns_409_problem_ignoring_case()
    {
        Start("Demo");

        var response = await Post(Dog("REX"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Json(response))["title"]!.GetValue<string>().Should().Be("Name taken");
    }

    [Test]
    public async Task CreatePet_breaking_schema_constraints_returns_400_validation_problem()
    {
        Start("Empty");

        var response = await Post(new JsonObject
        {
            ["name"] = "",
            ["tag"] = "NOT LOWER",
            ["details"] = new JsonObject { ["kind"] = "cat", ["livesLeft"] = 12 },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var errors = (await Json(response))["errors"]!.AsObject();
        errors.Select(e => e.Key).Order().Should().Equal("details.livesLeft", "name", "tag");
    }

    [Test]
    public async Task CreatePet_missing_a_required_property_returns_400()
    {
        Start("Empty");

        var response = await Post(new JsonObject { ["name"] = "Rex" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreatePet_accepts_the_discriminator_anywhere_in_the_object()
    {
        Start("Empty");

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
        Start("Demo");

        var response = await _client.GetAsync("/pets/2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = (await Json(response))["details"]!;
        details["kind"]!.GetValue<string>().Should().Be("cat");
        details["livesLeft"]!.GetValue<int>().Should().Be(7);
    }

    [Test]
    public async Task GetPet_that_does_not_exist_returns_404_problem()
    {
        Start("Demo");

        var response = await _client.GetAsync("/pets/42");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["detail"]!.GetValue<string>().Should().Be("There is no pet 42.");
    }

    [Test]
    public async Task UpsertPet_creates_with_201_then_replaces_with_200()
    {
        Start("Empty");

        var created = await _client.PutAsJsonAsync("/pets/7", Dog("Rex"));
        var replaced = await _client.PutAsJsonAsync("/pets/7", Dog("Max"));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        replaced.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(replaced))["name"]!.GetValue<string>().Should().Be("Max");
        (await Json(await _client.GetAsync("/pets/7")))["name"]!.GetValue<string>().Should().Be("Max");
    }

    [Test]
    public async Task UpsertPet_keeping_its_own_name_is_not_a_conflict()
    {
        Start("Demo");

        var response = await _client.PutAsJsonAsync("/pets/1", Dog("Rex"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task UpsertPet_taking_another_pet_s_name_returns_409_problem()
    {
        Start("Demo");

        var response = await _client.PutAsJsonAsync("/pets/1", Dog("tom"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task DeletePet_returns_204_then_404()
    {
        Start("Demo");

        (await _client.DeleteAsync("/pets/1")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync("/pets/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync("/pets/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private void Start(string seed)
    {
        _app = new PetStoreApp(seed);
        _client = _app.CreateClient();
    }

    private static JsonObject Dog(string name)
        => new() { ["name"] = name, ["details"] = new JsonObject { ["kind"] = "dog", ["goodBoy"] = true } };

    private Task<HttpResponseMessage> Post(JsonObject body) => _client.PostAsJsonAsync("/pets", body);

    private static IEnumerable<string> Names(JsonArray pets) => pets.Select(p => p!["name"]!.GetValue<string>());

    private static async Task<JsonNode> Json(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
}
