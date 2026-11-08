using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using OneOf;
using PetStore.Api;
using PetStore;
using Resultful;

namespace Matchmark.Sample.Tests;

/// <summary>
/// One suite, written against the contract (<see cref="IPetsHandlers"/>), run against every implementation of it:
/// <list type="bullet">
/// <item><c>in-process</c>: the real handlers behind the generated validation decorator. No HTTP, no serialisation.</item>
/// <item><c>http</c>: the generated <see cref="PetStoreClient"/> against the running server, which also checks every
/// response against the spec (status, content type, schema).</item>
/// </list>
/// The same assertions passing on both shows the HTTP layer adds nothing and loses nothing. Pointing the client at
/// another server, such as a legacy implementation or one written in another language, runs this suite against it unchanged.
/// After the HTTP run the suite checks it reached every outcome the spec documents.
/// </summary>
[TestFixture("in-process")]
[TestFixture("http")]
public sealed class ContractSuite(string target)
{
    private static readonly HashSet<(string Operation, int Status)> Reached = [];

    private PetStoreApp _app = null!;
    private AsyncServiceScope? _scope;
    private HttpClient? _http;
    private PetStoreClient? _client;

    [TearDown]
    public async Task TearDown()
    {
        if (_client is not null)
        {
            lock (Reached)
            {
                Reached.UnionWith(_client.Observed);
                Reached.UnionWith(_client.Refused);
            }
        }

        _http?.Dispose();
        if (_scope is { } scope)
        {
            await scope.DisposeAsync();
        }

        await _app.DisposeAsync();
        (_http, _client, _scope) = (null, null, null);
    }

    /// <summary>Contract coverage: an outcome the spec documents but no test reaches is an untested behaviour.</summary>
    [OneTimeTearDown]
    public void Every_documented_outcome_was_reached()
    {
        if (target != "http")
        {
            return;
        }

        var missing = PetStoreContract.DocumentedOutcomes.Except(Reached).Select(o => $"{o.Operation} {o.Status}").ToList();
        TestContext.Progress.WriteLine($"Contract coverage: {PetStoreContract.DocumentedOutcomes.Count - missing.Count}/{PetStoreContract.DocumentedOutcomes.Count} documented outcomes reached.");
        missing.Should().BeEmpty("every outcome the spec documents should be exercised by the contract suite");
    }

    [Test]
    public async Task ListPets_returns_the_seeded_pets_in_id_order()
    {
        var pets = await Start("Demo").ListPets(null, default);

        pets.Select(p => p.Name).Should().Equal("Rex", "Tom", "Bella");
        pets[1].Details.Should().BeEquivalentTo(new Cat { LivesLeft = 7 });
    }

    [Test]
    public async Task CreatePet_returns_the_created_pet()
    {
        var result = await Start("Empty").CreatePet(Dog("Rex"), default);

        var pet = Success(result);
        pet.Should().BeEquivalentTo(new { Name = "Rex", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } });
    }

    [Test]
    public async Task CreatePet_with_a_taken_name_is_the_conflict_case()
    {
        var result = await Start("Demo").CreatePet(Dog("rex"), default);

        Case<CreatePetConflict>(Failure(result)).Body.Title.Should().Be("Name taken");
    }

    [Test]
    public async Task CreatePet_breaking_the_schema_is_the_validation_case()
    {
        var result = await Start("Empty").CreatePet(new NewPet { Name = "", Tag = "NOT LOWER", Details = new Cat { LivesLeft = 12 } }, default);

        Case<CreatePetBadRequest>(Failure(result)).Body.Errors.Keys.Should().BeEquivalentTo("name", "tag", "details.livesLeft");
    }

    [Test]
    public async Task GetPet_returns_the_pet()
    {
        var pet = Success(await Start("Demo").GetPet(2, default));

        pet.Name.Should().Be("Tom");
    }

    [Test]
    public async Task GetPet_that_does_not_exist_is_the_not_found_case()
    {
        var result = await Start("Demo").GetPet(42, default);

        Case<GetPetNotFound>(Failure(result)).Body.Detail.Should().Be("There is no pet 42.");
    }

    [Test]
    public async Task UpsertPet_creates_then_replaces()
    {
        var pets = Start("Empty");

        Case<UpsertPetCreated>(Success(await pets.UpsertPet(7, Dog("Rex"), default))).Body.Id.Should().Be(7);
        Case<UpsertPetOk>(Success(await pets.UpsertPet(7, Dog("Max"), default))).Body.Name.Should().Be("Max");
    }

    [Test]
    public async Task UpsertPet_taking_another_pet_s_name_is_the_conflict_case()
    {
        var result = await Start("Demo").UpsertPet(1, Dog("tom"), default);

        Case<UpsertPetConflict>(Failure(result));
    }

    [Test]
    public async Task UpsertPet_breaking_the_schema_is_the_validation_case()
    {
        var result = await Start("Demo").UpsertPet(1, Dog(""), default);

        Case<UpsertPetBadRequest>(Failure(result)).Body.Errors.Keys.Should().BeEquivalentTo("name");
    }

    [Test]
    public async Task DeletePet_succeeds_then_is_the_not_found_case()
    {
        var pets = Start("Demo");

        (await pets.DeletePet(1, default)).ToOneOf().Value.Should().BeOfType<Resultful.Unit>();
        Case<DeletePetNotFound>((await pets.DeletePet(1, default)).ToOneOf().Value.Should().BeOfType<DeletePetError>().Subject);
    }

    private IPetsHandlers Start(string seed)
    {
        _app = new PetStoreApp(seed);
        if (target == "http")
        {
            _http = _app.CreateClient();
            return _client = new PetStoreClient(_http);
        }

        _ = _app.Services; // starts the host, which creates and seeds the database
        var scope = _app.Services.CreateAsyncScope();
        _scope = scope;
        return new ValidatingPetsHandlers(new PetsHandlers(scope.ServiceProvider.GetRequiredService<PetRepository>()));
    }

    private static NewPet Dog(string name) => new() { Name = name, Details = new Dog { GoodBoy = true } };

    private static T Success<T, TError>(Result<T, TError> result) => result.ToOneOf().Value.Should().BeOfType<T>().Subject;

    private static TError Failure<T, TError>(Result<T, TError> result) => result.ToOneOf().Value.Should().BeOfType<TError>().Subject;

    /// <summary>Asserts which case a union holds and returns it.</summary>
    private static T Case<T>(IOneOf union) => union.Value.Should().BeOfType<T>().Subject;
}
