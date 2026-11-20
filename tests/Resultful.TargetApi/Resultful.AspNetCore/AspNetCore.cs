#nullable enable
using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;

// M5 (20a, 20b). The core knows nothing about HTTP; this package maps the user's error types.
// The map (DECISIONS-TAKEN 04c, accepted): ValidationFailed -> 400 ValidationProblemDetails; types the user maps
// (not found -> 404, conflict -> 409, …); anything else -> 500 ProblemDetails. The error is walked outside-in
// (the error, then its Cause chain and Inner errors) and the first mapped type wins. A 500 body has a fixed title and
// no Message outside the Development environment (HTTP-500-NOLEAK).
namespace Resultful.AspNetCore
{
    public sealed class ResultfulHttpOptions
    {
        public ResultfulHttpOptions() => throw null!;

        // errors.Map<OrderNotFound>(StatusCodes.Status404NotFound)
        public ResultfulHttpOptions Map<TError>(int statusCode) where TError : Error => throw null!;

        // The key of each entry in a validation problem's "errors". Defaults to the error's type name.
        public Func<Error, string> ValidationKey { get => throw null!; set => throw null!; }

        // The status for an error no entry maps. 500 by default.
        public int DefaultStatusCode { get => throw null!; set => throw null!; }
    }

    public static class ResultfulServiceCollectionExtensions
    {
        public static IServiceCollection AddResultful(this IServiceCollection services) => throw null!;
        public static IServiceCollection AddResultful(this IServiceCollection services, Action<ResultfulHttpOptions> configure) => throw null!;
    }

    // An IResult that maps a result when it executes, using the registered options and IProblemDetailsService.
    // Its metadata lists the success status and each failure status, so the OpenAPI document shows them. It keeps the
    // result's error type (DECISIONS-TAKEN 20a, #39 question 11), so the metadata can read the statuses a route can
    // answer from TError: a Validation's ValidationFailed<TError> is a 400; any error may be a 500.
    public sealed class ResultHttpResult<T, TError> : IResult, IEndpointMetadataProvider where TError : Error
    {
        internal ResultHttpResult() => throw null!;
        public Task ExecuteAsync(HttpContext httpContext) => throw null!;
        public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder) => throw null!;
    }

    // A result whose error is a union (decision 17) widens first: result.AsResult().ToHttpResult().
    public static class HttpResultExtensions
    {
        public static ResultHttpResult<T, Error> ToHttpResult<T>(this Result<T> result) => throw null!;
        public static ResultHttpResult<T, TError> ToHttpResult<T, TError>(this Result<T, TError> result) where TError : Error => throw null!;
        public static ResultHttpResult<T, ValidationFailed<Error>> ToHttpResult<T>(this Validation<T> result) => throw null!;
        public static ResultHttpResult<T, ValidationFailed<TError>> ToHttpResult<T, TError>(this Validation<T, TError> result) where TError : Error => throw null!;
    }

    // Lets a Minimal API handler return a result directly: the filter turns it into a ResultHttpResult.
    public sealed class ResultEndpointFilter : IEndpointFilter
    {
        public ResultEndpointFilter() => throw null!;
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => throw null!;
    }

    public static class ResultfulEndpointConventionBuilderExtensions
    {
        public static TBuilder WithResultful<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder => throw null!;
    }

    // The bridge from .NET's Minimal API validation (net10.0 and later) to Validation: its failures become
    // ValidationFailed and answer 400 like any other.
    public sealed record MinimalApiValidationError : Error
    {
        public MinimalApiValidationError(string field, string message) : base(message) => throw null!;
        public string Field => throw null!;
    }
}
