using FluentAssertions;
using OneOf;
using NUnit.Framework;
using PetStore;
using PetStore.Api;

namespace Resultful.OpenApi.Sample.Tests;

/// <summary>
/// The handlers are plain Resultful code: tested without HTTP, against the generated contract types.
/// </summary>
[TestFixture]
public sealed class HandlerTests
{
    private PetRepository _repository = null!;
    private PetsHandlers _handlers = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new PetRepository();
        _handlers = new PetsHandlers(_repository);
    }

    [Test]
    public async Task CreatePet_with_a_taken_name_is_the_spec_s_409_case()
    {
        await _handlers.CreatePet(NewDog("Rex"), CancellationToken.None);

        var result = await _handlers.CreatePet(NewDog("Rex"), CancellationToken.None);

        Case<CreatePetConflict>(Case<CreatePetError>(result.ToOneOf())).Body.Status.Should().Be(409);
    }

    [Test]
    public async Task GetPet_that_does_not_exist_is_the_spec_s_404_case()
    {
        var result = await _handlers.GetPet(1, CancellationToken.None);

        Case<GetPetNotFound>(Case<GetPetError>(result.ToOneOf())).Body.Status.Should().Be(404);
    }

    [Test]
    public async Task UpsertPet_reports_which_2xx_it_is()
    {
        var created = await _handlers.UpsertPet(3, NewDog("Rex"), CancellationToken.None);
        var replaced = await _handlers.UpsertPet(3, NewDog("Max"), CancellationToken.None);

        Case<UpsertPetCreated>(created).Body.Name.Should().Be("Rex");
        Case<UpsertPetOk>(replaced).Body.Name.Should().Be("Max");
    }

    /// <summary>Asserts which case a union holds and returns it.</summary>
    private static T Case<T>(IOneOf union) => union.Value.Should().BeOfType<T>().Subject;

    private static NewPet NewDog(string name) => new() { Name = name, Details = new Dog { GoodBoy = true } };
}
