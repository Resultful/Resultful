using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using PetStore.Api;
using PetStore.Scaffolded;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The Pet Store as Matchmark.Cli writes it, untouched: [ApiController] controllers and a reference
/// implementation that answers with the spec's examples. Before anyone edits it, it already describes and answers
/// exactly as the spec says.
/// </summary>
[TestFixture]
public sealed class ScaffoldedServerTests
{
    [Test]
    public async Task The_scaffolded_controllers_describe_themselves_exactly_as_the_spec_does()
    {
        await using var server = new WebApplicationFactory<PetsController>();
        var served = JsonNode.Parse(await server.CreateClient().GetStringAsync("/openapi/v1.json"))!;

        var differences = new SpecComparer(await SpecDriftTests.LoadSpec(), served).Compare();

        string.Join("\n", differences).Should().BeEmpty();
    }

    [Test]
    public async Task Every_reference_answer_conforms_to_the_spec()
    {
        await using var server = new WebApplicationFactory<PetsController>();
        var pets = new PetStoreClient(server.CreateClient());
        var rex = new NewPet { Name = "Rex", Details = new Dog { GoodBoy = true } };

        // The client throws ContractViolationException for any answer the spec doesn't allow.
        (await pets.ListPets(null, default)).Should().ContainSingle();
        (await pets.CreatePet(rex, default)).ToOneOf().Value.Should().BeOfType<Pet>();
        (await pets.GetPet(1, default)).ToOneOf().Value.Should().BeOfType<Pet>().Which.Name.Should().Be("Rex", "getPet answers with the spec's example");
        (await pets.UpsertPet(1, rex, default)).ToOneOf().Value.Should().BeOfType<UpsertPetSuccess>();
        (await pets.DeletePet(1, default)).ToOneOf().Value.Should().BeOfType<Resultful.Unit>();
    }

    [Test]
    public async Task The_scaffolded_controllers_validate_requests_from_the_spec()
    {
        await using var server = new WebApplicationFactory<PetsController>();
        var pets = new PetStoreClient(server.CreateClient());

        var result = await pets.CreatePet(new NewPet { Name = "", Tag = "NOT LOWER", Details = new Cat { LivesLeft = 12 } }, default);

        result.ToOneOf().Value.Should().BeOfType<CreatePetError>().Which.Value.Should().BeOfType<CreatePetBadRequest>()
            .Which.Body.Errors.Keys.Should().BeEquivalentTo("name", "tag", "details.livesLeft");
    }
}
