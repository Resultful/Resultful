// Scaffolded from petstore.yaml by symbolon scaffold. This file is yours: edit it freely, it is never
// regenerated. The contract it implements (the types, unions and handler interfaces) stays generated.
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resultful;
using PetStore.Api;

namespace PetStore.Scaffolded;

/// <summary>
/// The reference implementation of <see cref="IPetsHandlers"/>: every operation answers with its first documented
/// success, built from the spec's example (or a sample that passes the schema). Replace each body with the real behaviour;
/// the contract suite and outcome coverage show what is still a reference answer.
/// </summary>
public sealed class PetsHandlers : IPetsHandlers
{
    /// <summary><c>GET /pets</c>. Documented outcomes: 200 Ok.</summary>
    public Task<global::System.Collections.Generic.IReadOnlyList<Pet>> ListPets(int? limit, CancellationToken cancellationToken)
    {
        // TODO: replace this reference answer (the spec's 200 Ok) with the real behaviour.
        global::System.Collections.Generic.IReadOnlyList<Pet> answer = [new Pet { Id = 1, Name = "x", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } }];
        return Task.FromResult<global::System.Collections.Generic.IReadOnlyList<Pet>>(answer);
    }

    /// <summary><c>POST /pets</c>. Documented outcomes: 201 Created, 400 BadRequest, 409 Conflict.</summary>
    public Task<Result<Pet, CreatePetError>> CreatePet(NewPet body, CancellationToken cancellationToken)
    {
        // TODO: replace this reference answer (the spec's 201 Created) with the real behaviour.
        Pet answer = new Pet { Id = 1, Name = "x", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } };
        return Task.FromResult<Result<Pet, CreatePetError>>(global::Resultful.Result.Ok<Pet, CreatePetError>(answer));
    }

    /// <summary><c>GET /pets/{petId}</c>. Documented outcomes: 200 Ok, 404 NotFound.</summary>
    public Task<Result<Pet, GetPetError>> GetPet(long petId, CancellationToken cancellationToken)
    {
        // TODO: replace this reference answer (the spec's 200 Ok) with the real behaviour.
        Pet answer = new Pet { Id = 1, Name = "Rex", Tag = "good-dog", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } };
        return Task.FromResult<Result<Pet, GetPetError>>(global::Resultful.Result.Ok<Pet, GetPetError>(answer));
    }

    /// <summary><c>PUT /pets/{petId}</c>. Documented outcomes: 200 Ok, 201 Created, 400 BadRequest, 409 Conflict.</summary>
    public Task<Result<UpsertPetSuccess, UpsertPetError>> UpsertPet(long petId, NewPet body, CancellationToken cancellationToken)
    {
        // TODO: replace this reference answer (the spec's 200 Ok) with the real behaviour.
        Pet answer = new Pet { Id = 1, Name = "x", Status = PetStatus.Available, Details = new Dog { GoodBoy = true } };
        return Task.FromResult<Result<UpsertPetSuccess, UpsertPetError>>(global::Resultful.Result.Ok<UpsertPetSuccess, UpsertPetError>(UpsertPetSuccess.Ok(answer)));
    }

    /// <summary><c>DELETE /pets/{petId}</c>. Documented outcomes: 204 NoContent, 404 NotFound.</summary>
    public Task<VoidResult<DeletePetError>> DeletePet(long petId, CancellationToken cancellationToken)
    {
        // TODO: replace this reference answer (the spec's 204 NoContent) with the real behaviour.
        return Task.FromResult<VoidResult<DeletePetError>>(global::Resultful.Result.Ok<DeletePetError>());
    }
}
