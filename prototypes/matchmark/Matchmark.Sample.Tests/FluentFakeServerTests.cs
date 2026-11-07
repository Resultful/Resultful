using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using PetStore.Api;
using Resultful;
using Matchmark.Fakes;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The fluent fake: a real server on a free port, set up with <c>Given</c> and checked with <c>Should()</c>, typed per
/// operation from the spec. Answers are named after the spec's responses, so only documented ones can be given.
/// </summary>
[TestFixture]
public sealed class FluentFakeServerTests
{
    private static readonly Pet Rex = new() { Id = 7, Name = "Rex", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } };
    private static readonly NewPet NewRex = new() { Name = "Rex", Details = new Dog { GoodBoy = true } };

    private PetStoreFakeServer _server = null!;
    private PetStoreClient _pets = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await PetStoreFakeServer.StartAsync();
        _pets = _server.CreateClient();
    }

    [TearDown]
    public async Task TearDown() => await _server.DisposeAsync();

    // ------------------------------------------------------------------ Given

    [Test]
    public async Task It_is_a_real_server_on_a_real_port()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_server.BaseAddress, "pets/1"));

        _server.BaseAddress.Port.Should().BeGreaterThan(0);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"name\":\"Rex\""); // the spec's example
    }

    [Test]
    public async Task Answers_are_named_after_the_spec_s_responses_and_run_in_sequence()
    {
        _server.Given.GetPet(petId: 7).Returns.Ok(Rex).Then.NotFound(new ProblemDetails { Title = "Adopted" });

        var first = await _pets.GetPet(7, default);
        var second = await _pets.GetPet(7, default);
        var third = await _pets.GetPet(7, default);

        first.ToOneOf().Value.Should().BeEquivalentTo(Rex);
        Case<GetPetNotFound>(second).Body.Title.Should().Be("Adopted");
        Case<GetPetNotFound>(third).Body.Title.Should().Be("Adopted", "the last answer repeats");
    }

    [Test]
    public async Task Rules_match_arguments_by_equality_or_by_predicate_and_the_latest_wins()
    {
        _server.Given.GetPet().Returns.NotFound();
        _server.Given.GetPet(where: id => id > 100).Returns.Ok(Rex with { Id = 101 });
        _server.Given.GetPet(petId: 7).Returns.Ok(Rex);

        (await _pets.GetPet(7, default)).ToOneOf().Value.Should().BeOfType<Pet>().Which.Id.Should().Be(7);
        (await _pets.GetPet(500, default)).ToOneOf().Value.Should().BeOfType<Pet>().Which.Id.Should().Be(101);
        Case<GetPetNotFound>(await _pets.GetPet(1, default));
    }

    [Test]
    public async Task A_request_body_can_be_matched_by_value()
    {
        _server.Given.CreatePet(body: NewRex).Returns.Conflict(new ProblemDetails { Title = "Rex exists" });

        var rex = await _pets.CreatePet(NewRex, default);
        var max = await _pets.CreatePet(NewRex with { Name = "Max" }, default);

        rex.ToOneOf().Value.Should().BeOfType<CreatePetError>().Which.Value.Should().BeOfType<CreatePetConflict>();
        max.ToOneOf().Value.Should().BeOfType<Pet>("an unmatched call gets the default answer");
    }

    [Test]
    public async Task NothingElse_makes_unmatched_calls_fail()
    {
        _server.Given.GetPet(petId: 7).Returns.Ok(Rex);
        _server.Given.NothingElse();

        var response = await _server.CreateHttpClient().GetAsync("pets/8");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("getPet(petId: 8) matched no Given");
    }

    [Test]
    public void An_answer_that_breaks_the_schema_fails_when_it_is_given()
    {
        var act = () => _server.Given.GetPet().Returns.Ok(Rex with { Name = "" });

        act.Should().Throw<ArgumentException>().WithMessage("The body breaks the schema: name: Must be at least 1 characters.");
    }

    // ------------------------------------------------------------------ Should

    [Test]
    public async Task HaveReceived_counts_matching_calls()
    {
        await _pets.GetPet(7, default);
        await _pets.GetPet(7, default);
        await _pets.CreatePet(NewRex, default);

        _server.Should().HaveReceived.GetPet(petId: 7).Twice();
        _server.Should().HaveReceived.CreatePet(where: body => body.Name == "Rex").Once();
        _server.Should().HaveReceived.DeletePet().Never();
        _server.Should().HaveReceivedNoOtherCalls();
    }

    [Test]
    public async Task A_failed_HaveReceived_says_what_was_expected_and_what_was_received()
    {
        await _pets.GetPet(1, default);
        await _pets.DeletePet(7, default);

        var act = () => _server.Should().HaveReceived.GetPet(petId: 7).Once();

        act.Should().Throw<FakeAssertionException>().WithMessage(
            "Expected the fake to have received getPet(petId: 7) once, but it received it 0 times.\n" +
            "It received:\n  1. getPet(petId: 1)\n  2. deletePet(petId: 7)");
    }

    [Test]
    public async Task HaveReceivedInOrder_checks_the_order_of_calls()
    {
        await _pets.CreatePet(NewRex, default);
        await _pets.GetPet(1, default);
        await _pets.DeletePet(1, default);

        _server.Should().HaveReceivedInOrder(c => c.CreatePet(), c => c.DeletePet(petId: 1));

        var act = () => _server.Should().HaveReceivedInOrder(c => c.DeletePet(), c => c.CreatePet());
        act.Should().Throw<FakeAssertionException>().WithMessage(
            "Expected the fake to have received deletePet(), then createPet(), but createPet() was only received before deletePet().*");
    }

    [Test]
    public async Task HaveReceivedNoOtherCalls_lists_the_calls_nothing_asserted_on()
    {
        await _pets.GetPet(1, default);
        await _pets.ListPets(5, default);

        _server.Should().HaveReceived.GetPet().Once();
        var act = () => _server.Should().HaveReceivedNoOtherCalls();

        act.Should().Throw<FakeAssertionException>().WithMessage(
            "Expected the fake to have received no other calls, but it received 1 more:\n  listPets(limit: 5)");
    }

    [Test]
    public async Task A_scenario_and_the_fluent_assertions_work_together()
    {
        _server.Fake.UseScenario(PetStoreScenario.InOrder().CreatePet(status: 201).GetPet(expect: new { petId = 1 }).Build());

        await _pets.CreatePet(NewRex, default);

        var act = () => _server.Should().HaveMetItsScenario();
        act.Should().Throw<FakeAssertionException>().WithMessage(
            "Expected the fake to have met its scenario, but:\n  - step 2 (getPet → 200 expecting {\"petId\":1}) was called 0 of 1 times");
    }

    private static GetPetNotFound Case<T>(Resultful.Result<Pet, GetPetError> result)
        => result.ToOneOf().Value.Should().BeOfType<GetPetError>().Which.Value.Should().BeOfType<GetPetNotFound>().Subject;
}
