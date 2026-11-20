using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Resultful.AspNetCore;

/// <summary>The error-to-status map and the ProblemDetails it writes (RFC 9457).</summary>
internal static class ErrorProblems
{
    /// <summary>The title of a 500, whatever the error: ASP.NET Core's own, so nothing about the error leaks.</summary>
    internal const string ServerErrorTitle = "An error occurred while processing your request.";

    private static readonly ResultfulHttpOptions Defaults = new();

    /// <summary>The status for <paramref name="error"/>, the error that decided it, and whether it is a validation failure.</summary>
    internal static (int StatusCode, Error Matched, bool Validation) Resolve(Error error, ResultfulHttpOptions options)
    {
        foreach (var candidate in error.Causes())
        {
            var type = candidate.GetType();
            if (options.TryGetStatus(type, out var exact)) return (exact, candidate, IsValidationFailed(type));
            if (IsValidationFailed(type)) return (StatusCodes.Status400BadRequest, candidate, true);
            for (var baseType = type.BaseType; baseType is not null && baseType != typeof(object); baseType = baseType.BaseType)
            {
                if (options.TryGetStatus(baseType, out var inherited)) return (inherited, candidate, false);
            }
        }

        return (options.DefaultStatusCode, error, false);
    }

    internal static bool IsValidationFailed(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValidationFailed<>);

    /// <summary>Builds the problem for <paramref name="error"/> as the request in <paramref name="httpContext"/> sees it.</summary>
    internal static ProblemDetails Create(HttpContext httpContext, Error error)
    {
        var services = httpContext.RequestServices;
        var options = services?.GetService<IOptions<ResultfulHttpOptions>>()?.Value ?? Defaults;
        var (statusCode, matched, validation) = Resolve(error, options);

        ProblemDetails problem;
        if (validation)
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var group in matched.Inner().GroupBy(options.ValidationKey, StringComparer.Ordinal))
                errors[group.Key] = group.Select(static e => e.Message).ToArray();
            problem = new HttpValidationProblemDetails(errors) { Status = statusCode };
        }
        else if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            // Outside Development a 5xx says nothing about the error (HTTP-500-NOLEAK). No host environment counts as
            // Production: the safe side.
            var development = services?.GetService<IHostEnvironment>()?.IsDevelopment() == true;
            problem = new ProblemDetails
            {
                Status = statusCode,
                Title = statusCode == StatusCodes.Status500InternalServerError ? ServerErrorTitle : null,
                Detail = development ? error.ToString() : null,
            };
        }
        else
        {
            problem = new ProblemDetails { Status = statusCode, Detail = matched.Message };
        }

        if (httpContext.Request.Path.HasValue) problem.Instance = httpContext.Request.Path.Value;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        return problem;
    }

    /// <summary>
    /// Writes the problem through <see cref="IProblemDetailsService"/> when it is registered (and the request accepts
    /// JSON), else as JSON with ASP.NET Core's source-generated metadata. Either way the status is set.
    /// </summary>
    internal static Task WriteAsync(HttpContext httpContext, Error error) =>
        TypedResults.Problem(Create(httpContext, error)).ExecuteAsync(httpContext);
}
