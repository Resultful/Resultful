using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Resultful.AspNetCore;

/// <summary>
/// A result as an <see cref="IResult"/>. A success answers 200 with the value (200 with no body for
/// <see cref="Unit"/>); a failure answers the status the registered <see cref="ResultfulHttpOptions"/> map gives,
/// with an RFC 9457 ProblemDetails body.
/// </summary>
/// <typeparam name="T">The success value's type.</typeparam>
/// <typeparam name="TError">
/// The result's error type, kept so an endpoint's metadata can list the statuses it can answer: a
/// <see cref="ValidationFailed{TError}"/> is a 400; any error may be a 500.
/// </typeparam>
/// <remarks>Made by <see cref="HttpResultExtensions"/>. The map is read when the result executes, not when it is made.</remarks>
public sealed class ResultHttpResult<T, TError> : IResult, IEndpointMetadataProvider where TError : Error
{
    private static readonly string[] JsonContentTypes = ["application/json"];
    private static readonly string[] ProblemContentTypes = ["application/problem+json"];

    private readonly T _value;
    private readonly TError? _error;

    internal ResultHttpResult(T value) => _value = value;

    internal ResultHttpResult(TError error)
    {
        _value = default!;
        _error = error;
    }

    /// <summary>Writes the response: the value on success, the mapped problem on failure.</summary>
    /// <param name="httpContext">The request's context; its services supply the options and the ProblemDetails service.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (_error is not null) return ErrorProblems.WriteAsync(httpContext, _error);
        return typeof(T) == typeof(Unit) ? TypedResults.Ok().ExecuteAsync(httpContext) : TypedResults.Ok(_value).ExecuteAsync(httpContext);
    }

    /// <summary>
    /// Adds the statuses this result can answer to an endpoint's metadata: 200 with <typeparamref name="T"/>, 400 with
    /// a validation problem when <typeparamref name="TError"/> can be a <see cref="ValidationFailed{TError}"/>, and
    /// 500 with a problem.
    /// </summary>
    /// <param name="method">The endpoint's handler.</param>
    /// <param name="builder">The endpoint's builder.</param>
    /// <remarks>The statuses of the map (404, 409, …) are configured at run time, so they are not listed here.</remarks>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(typeof(T) == typeof(Unit)
            ? new ProducesResponseTypeMetadata(StatusCodes.Status200OK)
            : new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(T), JsonContentTypes));
        if (typeof(TError).IsAssignableFrom(typeof(ValidationFailed<Error>)) || ErrorProblems.IsValidationFailed(typeof(TError)))
            builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(HttpValidationProblemDetails), ProblemContentTypes));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(ProblemDetails), ProblemContentTypes));
    }
}
