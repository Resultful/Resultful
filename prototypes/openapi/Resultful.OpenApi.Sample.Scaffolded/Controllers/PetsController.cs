// Scaffolded from petstore.yaml by Resultful.OpenApi.Scaffold. This file is yours: edit it freely, it is never
// regenerated. The contract it implements (the types, unions and handler interfaces) stays generated.
#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resultful;
using PetStore.Api;

namespace PetStore.Scaffolded;

/// <summary>
/// The <c>pets</c> operations as an ASP.NET Core controller. Each action calls <see cref="IPetsHandlers"/> and turns
/// its outcome into the documented response; the attributes describe exactly the responses the spec documents.
/// </summary>
[ApiController]
[Tags("pets")]
public sealed class PetsController(IPetsHandlers handlers) : ControllerBase
{
    /// <summary><c>GET /pets</c></summary>
    [HttpGet("/pets", Name = "listPets")]
    [ProducesResponseType<global::System.Collections.Generic.IReadOnlyList<Pet>>(200, "application/json")]
    public async Task<IActionResult> ListPets([FromQuery(Name = "limit")] int? limit, CancellationToken cancellationToken)
    {
        var value = await handlers.ListPets(limit, cancellationToken);
        return Ok(value);
    }

    /// <summary><c>POST /pets</c></summary>
    [HttpPost("/pets", Name = "createPet")]
    [Consumes("application/json")]
    [ProducesResponseType<Pet>(201, "application/json")]
    [ProducesResponseType<global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<global::Microsoft.AspNetCore.Mvc.ProblemDetails>(409, "application/problem+json")]
    public async Task<IActionResult> CreatePet([FromBody] NewPet body, CancellationToken cancellationToken)
    {
        if (PetStoreValidation.Validate(body) is { } errors)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var result = await handlers.CreatePet(body, cancellationToken);
        return result.Match<IActionResult>(
            success => Created((string?)null, success),
            error => error.Match<IActionResult>(@case => ValidationProblem(new ValidationProblemDetails(@case.Body.Errors) { Title = @case.Body.Title, Detail = @case.Body.Detail }), @case => ProblemResponse(@case.Body, 409)));
    }

    /// <summary><c>GET /pets/{petId}</c></summary>
    [HttpGet("/pets/{petId}", Name = "getPet")]
    [ProducesResponseType<Pet>(200, "application/json")]
    [ProducesResponseType<global::Microsoft.AspNetCore.Mvc.ProblemDetails>(404, "application/problem+json")]
    public async Task<IActionResult> GetPet([FromRoute(Name = "petId")] long petId, CancellationToken cancellationToken)
    {
        var result = await handlers.GetPet(petId, cancellationToken);
        return result.Match<IActionResult>(
            success => Ok(success),
            error => error.Match<IActionResult>(@case => ProblemResponse(@case.Body, 404)));
    }

    /// <summary><c>PUT /pets/{petId}</c></summary>
    [HttpPut("/pets/{petId}", Name = "upsertPet")]
    [Consumes("application/json")]
    [ProducesResponseType<Pet>(200, "application/json")]
    [ProducesResponseType<Pet>(201, "application/json")]
    [ProducesResponseType<global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<global::Microsoft.AspNetCore.Mvc.ProblemDetails>(409, "application/problem+json")]
    public async Task<IActionResult> UpsertPet([FromRoute(Name = "petId")] long petId, [FromBody] NewPet body, CancellationToken cancellationToken)
    {
        if (PetStoreValidation.Validate(body) is { } errors)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var result = await handlers.UpsertPet(petId, body, cancellationToken);
        return result.Match<IActionResult>(
            success => success.Match<IActionResult>(@case => Ok(@case.Body), @case => Created((string?)null, @case.Body)),
            error => error.Match<IActionResult>(@case => ValidationProblem(new ValidationProblemDetails(@case.Body.Errors) { Title = @case.Body.Title, Detail = @case.Body.Detail }), @case => ProblemResponse(@case.Body, 409)));
    }

    /// <summary><c>DELETE /pets/{petId}</c></summary>
    [HttpDelete("/pets/{petId}", Name = "deletePet")]
    [ProducesResponseType(204)]
    [ProducesResponseType<global::Microsoft.AspNetCore.Mvc.ProblemDetails>(404, "application/problem+json")]
    public async Task<IActionResult> DeletePet([FromRoute(Name = "petId")] long petId, CancellationToken cancellationToken)
    {
        var result = await handlers.DeletePet(petId, cancellationToken);
        return result.Match<IActionResult>(
            success => NoContent(),
            error => error.Match<IActionResult>(@case => ProblemResponse(@case.Body, 404)));
    }

    /// <summary>A problem as application/problem+json, with the status the spec gives it.</summary>
    private static ObjectResult ProblemResponse(ProblemDetails problem, int status)
    {
        problem.Status = status;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
