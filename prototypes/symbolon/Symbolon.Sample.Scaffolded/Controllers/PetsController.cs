// Scaffolded from petstore.yaml by symbolon scaffold. This file is yours: edit it freely, it is never
// regenerated. The contract it implements (the types, unions and handler interfaces) stays generated.
#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
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
            return ValidationProblem(modelStateDictionary: Invalid(errors));
        }

        var result = await handlers.CreatePet(body, cancellationToken);
        return result.Match<IActionResult>(
            success => Created((string?)null, success),
            error => error.Match<IActionResult>(@case => ValidationProblem(detail: @case.Body.Detail, title: @case.Body.Title, modelStateDictionary: Invalid(@case.Body.Errors)), @case => ProblemResponse(@case.Body, 409)));
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
            return ValidationProblem(modelStateDictionary: Invalid(errors));
        }

        var result = await handlers.UpsertPet(petId, body, cancellationToken);
        return result.Match<IActionResult>(
            success => success.Match<IActionResult>(@case => Ok(@case.Body), @case => Created((string?)null, @case.Body)),
            error => error.Match<IActionResult>(@case => ValidationProblem(detail: @case.Body.Detail, title: @case.Body.Title, modelStateDictionary: Invalid(@case.Body.Errors)), @case => ProblemResponse(@case.Body, 409)));
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

    /// <summary>
    /// A problem as application/problem+json, with the status the spec gives it, completed (type, trace id) the way MVC
    /// completes its own problems, so it reads the same as the generated endpoints' problems.
    /// </summary>
    private ObjectResult ProblemResponse(ProblemDetails problem, int status)
    {
        var completed = HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(HttpContext, status, problem.Title, problem.Type, problem.Detail, problem.Instance);
        foreach (var (key, value) in problem.Extensions)
        {
            completed.Extensions[key] = value;
        }

        return new ObjectResult(completed) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    /// <summary>Validation errors keyed by JSON path, for <see cref="ControllerBase.ValidationProblem()"/>.</summary>
    private static ModelStateDictionary Invalid(IDictionary<string, string[]> errors)
    {
        var state = new ModelStateDictionary();
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                state.AddModelError(key, message);
            }
        }

        return state;
    }
}
