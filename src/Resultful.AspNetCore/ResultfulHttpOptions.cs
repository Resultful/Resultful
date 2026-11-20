using Microsoft.AspNetCore.Http;

namespace Resultful.AspNetCore;

/// <summary>
/// How a failed result answers over HTTP: the error-to-status map, the keys of a validation problem, and the status
/// for an error nothing maps.
/// </summary>
/// <remarks>
/// <para>
/// The error is walked from the outside in: the error itself, then <see cref="Error.Causes"/> (its
/// <see cref="Error.Cause"/> chain and <see cref="Error.Inner"/> errors, depth first). The first error that has a
/// status wins. For one error, an entry for its own type wins over an entry for a base type, and the nearest base
/// type wins over a further one.
/// </para>
/// <para>
/// A <see cref="ValidationFailed{TError}"/> answers 400 with a validation problem unless an entry names its exact
/// type. Anything else answers <see cref="DefaultStatusCode"/>, 500 by default. A 4xx problem carries the matched
/// error's <see cref="Error.Message"/> as <c>detail</c>; a 5xx problem carries no message outside the Development
/// environment.
/// </para>
/// </remarks>
public sealed class ResultfulHttpOptions
{
    private readonly Dictionary<Type, int> _map = [];
    private Func<Error, string> _validationKey = static error => error.GetType().Name;
    private int _defaultStatusCode = StatusCodes.Status500InternalServerError;

    /// <summary>Creates options with an empty map, type-name validation keys and a default status of 500.</summary>
    public ResultfulHttpOptions()
    {
    }

    /// <summary>Answers <paramref name="statusCode"/> for an error of type <typeparamref name="TError"/>, or derived from it.</summary>
    /// <typeparam name="TError">The error type, often a leaf such as <c>OrderNotFound</c> or a closed hierarchy's base.</typeparam>
    /// <param name="statusCode">An error status, from 400 to 599. Mapping the same type again replaces its status.</param>
    /// <returns>These options, to chain more entries.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statusCode"/> is not between 400 and 599.</exception>
    public ResultfulHttpOptions Map<TError>(int statusCode) where TError : Error
    {
        _map[typeof(TError)] = CheckStatus(statusCode, nameof(statusCode));
        return this;
    }

    /// <summary>
    /// The key of each entry in a validation problem's <c>errors</c>. Defaults to the error's type name. Errors with
    /// the same key are listed together, in order.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public Func<Error, string> ValidationKey
    {
        get => _validationKey;
        set => _validationKey = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The status for an error no entry maps. 500 by default; from 400 to 599.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 400 and 599.</exception>
    public int DefaultStatusCode
    {
        get => _defaultStatusCode;
        set => _defaultStatusCode = CheckStatus(value, nameof(value));
    }

    internal bool TryGetStatus(Type type, out int statusCode) => _map.TryGetValue(type, out statusCode);

    private static int CheckStatus(int statusCode, string name) => statusCode is >= 400 and <= 599
        ? statusCode
        : throw new ArgumentOutOfRangeException(name, statusCode, "An error status is from 400 to 599.");
}
