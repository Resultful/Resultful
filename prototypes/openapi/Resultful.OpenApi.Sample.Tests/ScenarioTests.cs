using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using PetStore.Api;
using PetStore.Fake;
using Resultful.OpenApi.Fakes;

namespace Resultful.OpenApi.Sample.Tests;

/// <summary>
/// Scenarios: the calls a fake expects, in order or not, what each request must contain, and the answer to give.
/// Written in code (the generated, typed <see cref="PetStoreScenario"/>) or declaratively (JSON), both checked against
/// the spec. Run in-process, or stood up over HTTP as an isolated fake API per session.
/// </summary>
[TestFixture]
public sealed class ScenarioTests
{
    private static readonly NewPet Rex = new() { Name = "Rex", Details = new Dog { GoodBoy = true } };

    // ------------------------------------------------------------------ written in code

    [Test]
    public async Task Calls_made_as_the_scenario_expects_pass()
    {
        var fake = new PetStoreFake().UseScenario(PetStoreScenario.InOrder()
            .CreatePet(expect: new { body = new { name = "Rex" } }, status: 201)
            .GetPet(expect: new { petId = 1 })
            .DeletePet(expect: new { petId = 1 })
            .Build());

        await fake.CreatePet(Rex, default);
        await fake.GetPet(1, default);
        await fake.DeletePet(1, default);

        fake.Verify().Should().BeEquivalentTo(new { Passed = true, Failures = Array.Empty<string>() });
    }

    [Test]
    public async Task A_call_out_of_order_fails_and_says_which_step_comes_first()
    {
        var fake = new PetStoreFake().UseScenario(PetStoreScenario.InOrder()
            .CreatePet(status: 201)
            .GetPet()
            .Build());

        var act = () => fake.GetPet(1, default);

        (await act.Should().ThrowAsync<FakeExpectationException>()).WithMessage(
            "getPet was called out of order: step 2 (getPet → 200) comes after step 1 (createPet → 201)");
        fake.Verify().Failures.Should().HaveCount(3).And.Contain(f => f.Contains("step 1 (createPet → 201) was called 0 of 1 times"));
    }

    [Test]
    public async Task A_request_that_breaks_an_expectation_fails_at_the_exact_field()
    {
        var fake = new PetStoreFake().UseScenario(PetStoreScenario.InOrder()
            .CreatePet(expect: new { body = new { name = "Rex", details = new { kind = "cat" } } }, status: 201)
            .Build());

        var act = () => fake.CreatePet(Rex, default);

        (await act.Should().ThrowAsync<FakeExpectationException>()).WithMessage("*body.details.kind expected \"cat\", got \"dog\"");
    }

    [Test]
    public async Task Any_order_scenarios_accept_the_steps_in_any_order_and_count_times()
    {
        var fake = new PetStoreFake().UseScenario(PetStoreScenario.AnyOrder()
            .GetPet(expect: new { petId = 1 }, times: 2)
            .GetPet(expect: new { petId = 2 }, status: 404)
            .Build());

        await fake.GetPet(1, default);
        var second = await fake.GetPet(2, default);
        await fake.GetPet(1, default);

        second.ToOneOf().Value.Should().BeOfType<GetPetError>().Which.Value.Should().BeOfType<GetPetNotFound>();
        fake.Verify().Passed.Should().BeTrue();
    }

    [Test]
    public async Task Steps_never_called_fail_verification()
    {
        var fake = new PetStoreFake().UseScenario(PetStoreScenario.InOrder().CreatePet(status: 201).GetPet().Build());

        await fake.CreatePet(Rex, default);

        fake.Verify().Should().BeEquivalentTo(new
        {
            Passed = false,
            Failures = new[] { "step 2 (getPet → 200) was called 0 of 1 times" },
        });
    }

    [Test]
    public void A_scenario_the_spec_does_not_allow_will_not_build()
    {
        var act = () => PetStoreScenario.InOrder().GetPet(status: 500).Build();

        act.Should().Throw<ArgumentException>().WithMessage("*step 1 (getPet → 500): the spec documents no 500 response for 'getPet'*");
    }

    // ------------------------------------------------------------------ declarative

    [Test]
    public async Task A_declarative_scenario_runs_the_same_way()
    {
        var fake = new PetStoreFake().UseScenario(await Load("adopt-rex.json"));

        var created = await fake.CreatePet(Rex, default);
        await fake.GetPet(7, default);
        await fake.GetPet(7, default);
        await fake.DeletePet(7, default);
        var gone = await fake.GetPet(7, default);

        created.ToOneOf().Value.Should().BeOfType<Pet>().Which.Id.Should().Be(7);
        gone.ToOneOf().Value.Should().BeOfType<GetPetError>().Which.Value.Should().BeOfType<GetPetNotFound>()
            .Which.Body.Detail.Should().Be("There is no pet 7.");
        fake.Verify().Passed.Should().BeTrue();
    }

    [Test]
    public async Task Code_and_declarative_scenarios_are_the_same_model()
    {
        var fromCode = PetStoreScenario.InOrder()
            .GetPet(expect: new { petId = 7 }, status: 404)
            .Build();

        var fromJson = Scenario.FromJson(fromCode.ToJson());

        fromJson.ToJson().Should().Be(fromCode.ToJson());
        fromJson.ToJson().Should().Contain("\"petId\": 7");
        (await Load("adopt-rex.json")).Steps.Select(s => $"{s.Operation} {s.Status}x{s.Times}")
            .Should().Equal("createPet 201x1", "getPet 200x2", "deletePet 204x1", "getPet 404x1");
    }

    [Test]
    public async Task Everything_a_declarative_scenario_gets_wrong_is_reported_at_once()
    {
        var problems = PetStoreFake.Check(await Load("broken.json"));

        problems.Should().Equal(
            "step 1 (adoptPet → 200): the spec has no operation 'adoptPet'",
            "step 2 (getPet → 500): the spec documents no 500 response for 'getPet'",
            "step 3 (getPet → 200): 'id' is not a parameter; expected one of petId",
            "step 4 (getPet → 200): The body breaks the schema: name: Must be at least 1 characters.; details.livesLeft: Must be at most 9.",
            "step 5 (listPets → 200): times must be at least 1");
    }

    // ------------------------------------------------------------------ an API that stands up APIs

    [Test]
    public async Task A_session_is_an_isolated_fake_API_stood_up_from_a_scenario()
    {
        await using var server = new WebApplicationFactory<FakeServer>();
        var http = server.CreateClient();

        var session = await Start(http, await File.ReadAllTextAsync(ScenarioPath("adopt-rex.json")));
        var pets = new PetStoreClient(server.CreateClient(new() { BaseAddress = new Uri(http.BaseAddress!, session) }));

        (await pets.CreatePet(Rex, default)).ToOneOf().Value.Should().BeOfType<Pet>();
        await pets.GetPet(7, default);
        await pets.GetPet(7, default);
        await pets.DeletePet(7, default);
        await pets.GetPet(7, default);

        var report = await http.GetFromJsonAsync<JsonObject>($"/__fake{session.TrimEnd('/')}");
        report!["passed"]!.GetValue<bool>().Should().BeTrue();
        report["calls"]!.AsArray().Should().HaveCount(5);
        pets.Observed.Should().BeEquivalentTo([("createPet", 201), ("getPet", 200), ("deletePet", 204), ("getPet", 404)]);
    }

    [Test]
    public async Task Sessions_are_isolated_from_each_other_and_from_the_default_fake()
    {
        await using var server = new WebApplicationFactory<FakeServer>();
        var http = server.CreateClient();
        var gone = PetStoreScenario.AnyOrder().GetPet(status: 404, times: 10).Build().ToJson();

        var first = await Start(http, gone);
        var second = await Start(http, null);

        (await http.GetAsync(first + "pets/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await http.GetAsync(second + "pets/1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await http.GetAsync("/pets/1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await http.GetAsync("/sessions/nope/pets/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Over_HTTP_an_unexpected_call_is_a_500_problem_and_shows_in_the_report()
    {
        await using var server = new WebApplicationFactory<FakeServer>();
        var http = server.CreateClient();
        var session = await Start(http, PetStoreScenario.InOrder().CreatePet(status: 201).Build().ToJson());

        var response = await http.GetAsync(session + "pets/1");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["title"]!.GetValue<string>().Should().Be("Fake expectation failed");
        problem["detail"]!.GetValue<string>().Should().Be("getPet was not expected; the next step is 1 (createPet → 201)");

        var report = await http.GetFromJsonAsync<JsonObject>($"/__fake{session.TrimEnd('/')}");
        report!["passed"]!.GetValue<bool>().Should().BeFalse();
    }

    [Test]
    public async Task A_scenario_that_breaks_the_spec_is_refused_with_every_problem()
    {
        await using var server = new WebApplicationFactory<FakeServer>();

        var response = await server.CreateClient().PostAsync("/__fake/sessions", new StringContent(
            await File.ReadAllTextAsync(ScenarioPath("broken.json")), System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["detail"]!.GetValue<string>()
            .Split('\n').Should().HaveCount(5);
    }

    private static async Task<string> Start(HttpClient http, string? scenario)
    {
        var response = await http.PostAsync("/__fake/sessions", scenario is null ? null : new StringContent(scenario, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["baseUrl"]!.GetValue<string>();
    }

    private static string ScenarioPath(string name) => Path.Combine(AppContext.BaseDirectory, "Scenarios", name);

    private static async Task<Scenario> Load(string name) => Scenario.FromJson(await File.ReadAllTextAsync(ScenarioPath(name)));
}
