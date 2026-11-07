using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PetStore.Api;
using PetStore.Fake;
using Resultful.OpenApi.Fakes;

namespace Resultful.OpenApi.Sample.Tests;

/// <summary>
/// The generated fake, used in-process (as an <see cref="IPetsHandlers"/>) and as a server (the generated endpoints over
/// it, in <c>Resultful.OpenApi.Sample.Fake</c>). Nothing in either was written by hand except the host's Program.cs.
/// </summary>
[TestFixture]
public sealed class FakeTests
{
    // ------------------------------------------------------------------ in-process

    [Test]
    public async Task By_default_an_operation_answers_with_the_spec_s_example()
    {
        var result = await new PetStoreFake().GetPet(1, default);

        result.ToOneOf().Value.Should().BeEquivalentTo(new Pet
        {
            Id = 1, Name = "Rex", Tag = "good-dog", Status = PetStatus.Available, Details = new Dog { GoodBoy = true },
        });
    }

    [Test]
    public async Task Without_an_example_it_answers_with_a_sample_that_passes_the_schema()
    {
        var pets = await new PetStoreFake().ListPets(null, default);

        pets.Should().ContainSingle();
        PetStoreValidation.Validate(pets[0]).Should().BeNull();
    }

    [Test]
    public async Task A_test_scripts_any_outcome_with_a_typed_function()
    {
        var fake = new PetStoreFake()
            .OnGetPet(petId => GetPetError.NotFound(new ProblemDetails { Title = $"No pet {petId}" }));

        var result = await fake.GetPet(9, default);

        result.ToOneOf().Value.Should().BeOfType<GetPetError>().Which.Value
            .Should().BeOfType<GetPetNotFound>().Which.Body.Title.Should().Be("No pet 9");
    }

    [Test]
    public async Task It_records_calls_and_resets()
    {
        var fake = new PetStoreFake();
        fake.Respond("deletePet", 404).Should().BeTrue();

        await fake.DeletePet(3, default);
        fake.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FakeCall("deletePet", new Dictionary<string, object?> { ["petId"] = 3L }));

        fake.Reset();
        fake.Calls.Should().BeEmpty();
        (await fake.DeletePet(3, default)).ToOneOf().Value.Should().BeOfType<Resultful.Unit>();
    }

    [Test]
    public void Only_documented_outcomes_can_be_scripted()
    {
        var fake = new PetStoreFake();

        fake.Respond("getPet", 500).Should().BeFalse();
        fake.Respond("noSuchOperation", 200).Should().BeFalse();
    }

    // ------------------------------------------------------------------ as a server

    [Test]
    public async Task Every_documented_outcome_of_the_fake_server_conforms_to_the_spec()
    {
        await using var server = new WebApplicationFactory<FakeServer>();
        var fake = server.Services.GetRequiredService<PetStoreFake>();
        var client = new PetStoreClient(server.CreateClient());

        foreach (var (operation, status) in PetStoreContract.DocumentedOutcomes)
        {
            fake.Respond(operation, status).Should().BeTrue();
            await Call(client, operation); // throws ContractViolationException if the response breaks the spec
        }

        client.Observed.Should().BeEquivalentTo(PetStoreContract.DocumentedOutcomes);
    }

    [Test]
    public async Task The_fake_server_validates_requests_before_the_fake_sees_them()
    {
        await using var server = new WebApplicationFactory<FakeServer>();
        var client = new PetStoreClient(server.CreateClient());

        var result = await client.CreatePet(new NewPet { Name = "", Details = new Dog { GoodBoy = true } }, default);

        result.ToOneOf().Value.Should().BeOfType<CreatePetError>().Which.Value.Should().BeOfType<CreatePetBadRequest>();
        server.Services.GetRequiredService<PetStoreFake>().Calls.Should().BeEmpty();
    }

    [Test]
    public async Task Any_language_can_script_the_fake_server_over_HTTP()
    {
        await using var server = new WebApplicationFactory<FakeServer>();
        var http = server.CreateClient();

        var scripted = await http.PutAsJsonAsync("/__fake/getPet", new { status = 404, body = new { title = "Gone" } });
        var response = await http.GetAsync("/pets/7");
        var calls = await http.GetFromJsonAsync<JsonArray>("/__fake/calls");

        scripted.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["title"]!.GetValue<string>().Should().Be("Gone");
        calls!.Select(c => c!["operation"]!.GetValue<string>()).Should().Equal("getPet");
    }

    [Test]
    public async Task The_fake_server_describes_itself_exactly_as_the_spec_does()
    {
        await using var server = new WebApplicationFactory<FakeServer>();
        var served = JsonNode.Parse(await server.CreateClient().GetStringAsync("/openapi/v1.json"))!;

        var differences = new SpecComparer(await SpecDriftTests.LoadSpec(), served).Compare();

        string.Join("\n", differences).Should().BeEmpty();
    }

    private static Task Call(PetStoreClient client, string operation) => operation switch
    {
        "listPets" => client.ListPets(null, default),
        "createPet" => client.CreatePet(ValidPet, default),
        "getPet" => client.GetPet(1, default),
        "upsertPet" => client.UpsertPet(1, ValidPet, default),
        "deletePet" => client.DeletePet(1, default),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "add the new operation here"),
    };

    private static readonly NewPet ValidPet = new() { Name = "Rex", Details = new Dog { GoodBoy = true } };
}
