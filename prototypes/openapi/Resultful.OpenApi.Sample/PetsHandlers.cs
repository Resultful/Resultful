using Microsoft.AspNetCore.Mvc;
using PetStore.Api;
using Resultful;

namespace PetStore;

/// <summary>
/// The only hand-written API code. Each method's signature comes from the spec: the success type is the 2xx body,
/// the error type is a union of the documented error responses, and the generated endpoint maps both to HTTP.
/// </summary>
public sealed class PetsHandlers(PetRepository repository) : IPetsHandlers
{
    public Task<IReadOnlyList<Pet>> ListPets(int? limit, CancellationToken cancellationToken)
        => repository.List(limit ?? 100, cancellationToken);

    public Task<Result<Pet, CreatePetError>> CreatePet(NewPet body, CancellationToken cancellationToken)
        => UniqueName(body.Name, exceptId: null, cancellationToken)
            .MapAsync(_ => repository.Add(body, cancellationToken))
            .MapError(CreatePetError.Conflict);

    public Task<Result<Pet, GetPetError>> GetPet(long petId, CancellationToken cancellationToken)
        => repository.Find(petId, cancellationToken)
            .Match<Pet, Result<Pet, GetPetError>>(
                pet => pet,
                _ => GetPetError.NotFound(NotFound(petId)));

    public Task<Result<UpsertPetSuccess, UpsertPetError>> UpsertPet(long petId, NewPet body, CancellationToken cancellationToken)
        => UniqueName(body.Name, exceptId: petId, cancellationToken)
            .MapError(UpsertPetError.Conflict)
            .MapAsync(async _ =>
            {
                var existed = (await repository.Find(petId, cancellationToken)).Match(_ => true, _ => false);
                var saved = await repository.Save(petId, body, cancellationToken);
                return existed ? UpsertPetSuccess.Ok(saved) : UpsertPetSuccess.Created(saved);
            });

    public async Task<VoidResult<DeletePetError>> DeletePet(long petId, CancellationToken cancellationToken)
        => await repository.Remove(petId, cancellationToken)
            ? Unit.Value
            : DeletePetError.NotFound(NotFound(petId));

    private async Task<Result<string, ProblemDetails>> UniqueName(string name, long? exceptId, CancellationToken cancellationToken)
        => await repository.NameTaken(name, exceptId, cancellationToken)
            ? new ProblemDetails { Title = "Name taken", Status = 409, Detail = $"A pet called '{name}' already exists." }
            : name;

    private static ProblemDetails NotFound(long petId)
        => new() { Title = "Not found", Status = 404, Detail = $"There is no pet {petId}." };
}
