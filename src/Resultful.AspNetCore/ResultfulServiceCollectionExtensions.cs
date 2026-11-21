using Microsoft.Extensions.DependencyInjection;

namespace Resultful.AspNetCore;

/// <summary>Registers what a <see cref="ResultHttpResult{T, TError}"/> reads when it executes.</summary>
public static class ResultfulServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ResultfulHttpOptions"/> with its defaults, and the ProblemDetails service
    /// (<see cref="ProblemDetailsServiceCollectionExtensions.AddProblemDetails(IServiceCollection)"/>, which is safe to
    /// call more than once).
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <remarks>
    /// Nothing here uses reflection-based JSON: a problem is written through ASP.NET Core's own source-generated
    /// ProblemDetails metadata, so a native AOT app needs nothing more. A success value is written with the app's
    /// JSON options, like <see cref="Microsoft.AspNetCore.Http.TypedResults.Ok{TValue}(TValue)"/>.
    /// </remarks>
    public static IServiceCollection AddResultful(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddProblemDetails();
        services.AddOptions<ResultfulHttpOptions>();
        return services;
    }

    /// <summary>Registers what <see cref="AddResultful(IServiceCollection)"/> does, then configures the options.</summary>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Fills the map: <c>errors =&gt; errors.Map&lt;OrderNotFound&gt;(404)</c>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddResultful(this IServiceCollection services, Action<ResultfulHttpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddResultful();
        services.Configure(configure);
        return services;
    }
}
