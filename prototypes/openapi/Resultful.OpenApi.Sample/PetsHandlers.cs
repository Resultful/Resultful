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
        => Task.FromResult(repository.List(limit ?? 100));

    public Task<Result<Pet, CreatePetError>> CreatePet(NewPet body, CancellationToken cancellationToken)
        => Task.FromResult(
            UniqueName(body.Name)
                .Map(_ => repository.Add(body))
                .MapError(CreatePetError.Conflict));

    public Task<Result<Pet, GetPetError>> GetPet(long petId, CancellationToken cancellationToken)
        => Task.FromResult(
            repository.Find(petId)
                .Match<Result<Pet, GetPetError>>(
                    pet => pet,
                    _ => GetPetError.NotFound(NotFound(petId))));

    public Task<UpsertPetSuccess> UpsertPet(long petId, NewPet body, CancellationToken cancellationToken)
        => Task.FromResult(
            repository.Find(petId)
                .Match(
                    _ => UpsertPetSuccess.Ok(repository.Save(petId, body)),
                    _ => UpsertPetSuccess.Created(repository.Save(petId, body))));

    public Task<VoidResult<DeletePetError>> DeletePet(long petId, CancellationToken cancellationToken)
        => Task.FromResult<VoidResult<DeletePetError>>(
            repository.Remove(petId)
                ? Unit.Value
                : DeletePetError.NotFound(NotFound(petId)));

    private Result<string, ProblemDetails> UniqueName(string name)
        => repository.NameTaken(name)
            ? new ProblemDetails { Title = "Name taken", Status = 409, Detail = $"A pet called '{name}' already exists." }
            : name;

    private static ProblemDetails NotFound(long petId)
        => new() { Title = "Not found", Status = 404, Detail = $"There is no pet {petId}." };
}
