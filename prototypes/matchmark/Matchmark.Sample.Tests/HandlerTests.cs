using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using OneOf;
using PetStore;
using PetStore.Api;
using PetStore.Data;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The handlers are plain Resultful code, tested without HTTP against the generated contract types. They use the
/// app's EF Core setup on its own in-memory SQLite database, from the same seed scenarios.
/// </summary>
[TestFixture]
public sealed class HandlerTests
{
    private PetStoreApp _app = null!;
    private AsyncServiceScope _scope;
    private PetsHandlers _handlers = null!;

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task CreatePet_with_a_taken_name_is_the_spec_s_409_case()
    {
        await Start("Demo");

        var result = await _handlers.CreatePet(NewDog("rex"), CancellationToken.None);

        Case<CreatePetConflict>(Case<CreatePetError>(result.ToOneOf())).Body.Status.Should().Be(409);
    }

    [Test]
    public async Task CreatePet_stores_the_pet()
    {
        await Start("Empty");

        var result = await _handlers.CreatePet(NewDog("Rex"), CancellationToken.None);

        var pet = Case<Pet>(result.ToOneOf());
        var stored = await _scope.ServiceProvider.GetRequiredService<PetStoreDb>().Pets.SingleAsync();
        stored.Id.Should().Be(pet.Id);
        stored.Details.Should().BeOfType<Dog>();
    }

    [Test]
    public async Task GetPet_that_does_not_exist_is_the_spec_s_404_case()
    {
        await Start("Empty");

        var result = await _handlers.GetPet(1, CancellationToken.None);

        Case<GetPetNotFound>(Case<GetPetError>(result.ToOneOf())).Body.Status.Should().Be(404);
    }

    [Test]
    public async Task UpsertPet_reports_which_2xx_it_is()
    {
        await Start("Empty");

        var created = await _handlers.UpsertPet(3, NewDog("Rex"), CancellationToken.None);
        var replaced = await _handlers.UpsertPet(3, NewDog("Max"), CancellationToken.None);

        Case<UpsertPetCreated>(Case<UpsertPetSuccess>(created.ToOneOf())).Body.Name.Should().Be("Rex");
        Case<UpsertPetOk>(Case<UpsertPetSuccess>(replaced.ToOneOf())).Body.Name.Should().Be("Max");
    }

    [Test]
    public async Task DeletePet_that_does_not_exist_is_the_spec_s_404_case()
    {
        await Start("Empty");

        var result = await _handlers.DeletePet(1, CancellationToken.None);

        Case<DeletePetNotFound>(Case<DeletePetError>(result.ToOneOf()));
    }

    private async Task Start(string seed)
    {
        _app = new PetStoreApp(seed);
        _ = _app.Services; // starts the host, which creates and seeds the database
        _scope = _app.Services.CreateAsyncScope();
        _handlers = new PetsHandlers(_scope.ServiceProvider.GetRequiredService<PetRepository>());
        await Task.CompletedTask;
    }

    /// <summary>Asserts which case a union holds and returns it.</summary>
    private static T Case<T>(IOneOf union) => union.Value.Should().BeOfType<T>().Subject;

    private static NewPet NewDog(string name) => new() { Name = name, Details = new Dog { GoodBoy = true } };
}
