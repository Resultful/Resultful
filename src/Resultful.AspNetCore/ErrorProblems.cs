using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Resultful.AspNetCore;

/// <summary>The error-to-status map and the ProblemDetails it writes (RFC 9457).</summary>
internal static class ErrorProblems
{
    /// <summary>The title of a 500, whatever the error: ASP.NET Core's own, so nothing about the error leaks.</summary>
    internal const string ServerErrorTitle = "An error occurred while processing your request.";

    private static readonly ResultfulHttpOptions Defaults = new();

    /// <summary>
    /// The status for <paramref name="error"/>, the error that decided it, whether that is a validation failure, and
    /// whether anything matched (an entry, or the built-in 400) rather than <see cref="ResultfulHttpOptions.DefaultStatusCode"/>.
    /// </summary>
    /// <remarks>
    /// The built-in 400 applies only when <paramref name="error"/> itself is the <see cref="ValidationFailed{TError}"/>.
    /// Deeper in the walk only explicit entries match, so a server error that wraps a validation failure stays a
    /// server error and doesn't show the validation messages.
    /// </remarks>
    internal static (int StatusCode, Error Matched, bool Validation, bool IsMapped) Resolve(Error error, ResultfulHttpOptions options)
    {
        foreach (var candidate in error.Causes())
        {
            var type = candidate.GetType();
            if (options.TryGetStatus(type, out var exact)) return (exact, candidate, IsValidationFailed(type), true);
            if (ReferenceEquals(candidate, error) && IsValidationFailed(type)) return (StatusCodes.Status400BadRequest, candidate, true, true);
            for (var baseType = type.BaseType; baseType is not null && baseType != typeof(object); baseType = baseType.BaseType)
            {
                if (options.TryGetStatus(baseType, out var inherited)) return (inherited, candidate, false, true);
            }
        }

        return (options.DefaultStatusCode, error, false, false);
    }

    internal static bool IsValidationFailed(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValidationFailed<>);

    /// <summary>Builds the problem for <paramref name="error"/> as the request in <paramref name="httpContext"/> sees it.</summary>
    internal static ProblemDetails Create(HttpContext httpContext, Error error)
    {
        var services = httpContext.RequestServices;
        var options = services?.GetService<IOptions<ResultfulHttpOptions>>()?.Value ?? Defaults;
        var (statusCode, matched, validation, isMapped) = Resolve(error, options);

        ProblemDetails problem;
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            // Checked first, so nothing reaches a 5xx body: not Message, not a validation failure's messages.
            // Outside Development a 5xx says nothing about the error (HTTP-500-NOLEAK). No host environment counts as
            // Production: the safe side. The error is logged instead.
            var development = services?.GetService<IHostEnvironment>()?.IsDevelopment() == true;
            LogServerError(services, statusCode, error);
            problem = new ProblemDetails
            {
                Status = statusCode,
                Title = statusCode == StatusCodes.Status500InternalServerError ? ServerErrorTitle : null,
                Detail = development ? error.ToString() : null,
            };
        }
        else if (validation)
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var group in matched.Inner().GroupBy(e => options.ValidationKey(e) ?? "", StringComparer.Ordinal))
                errors[group.Key] = group.Select(static e => e.Message).ToArray();
            problem = new HttpValidationProblemDetails(errors) { Status = statusCode };
        }
        else
        {
            // Only an error the map chose shows its Message. A 4xx DefaultStatusCode doesn't make every unmapped
            // error's Message public.
            problem = new ProblemDetails { Status = statusCode, Detail = isMapped ? matched.Message : null };
        }

        var instance = httpContext.Request.PathBase.Add(httpContext.Request.Path);
        if (instance.HasValue) problem.Instance = instance.Value;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        return problem;
    }

    private static readonly Action<ILogger, int, string, Exception?> ServerError = LoggerMessage.Define<int, string>(
        LogLevel.Error, new EventId(1, "ResultServerError"), "A result failed with status {StatusCode}: {Error}");

    /// <summary>
    /// Logs a 5xx once, at Error, with <see cref="Error.ToString"/>: the type name, message, context and cause chain,
    /// never the leaf's own properties (ERR-TOSTRING).
    /// </summary>
    private static void LogServerError(IServiceProvider? services, int statusCode, Error error)
    {
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger(LogCategory);
        if (logger is not null) ServerError(logger, statusCode, error.ToString(), null);
    }

    /// <summary>The log category of a 5xx.</summary>
    internal const string LogCategory = "Resultful.AspNetCore.ResultHttpResult";

    /// <summary>
    /// Writes the problem through <see cref="IProblemDetailsService"/> when it is registered (and the request accepts
    /// JSON), else as JSON with ASP.NET Core's source-generated metadata. Either way the status is set.
    /// </summary>
    internal static Task WriteAsync(HttpContext httpContext, Error error) =>
        TypedResults.Problem(Create(httpContext, error)).ExecuteAsync(httpContext);
}
