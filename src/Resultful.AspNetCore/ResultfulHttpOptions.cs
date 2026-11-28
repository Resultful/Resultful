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
/// A <see cref="ValidationFailed{TError}"/> answers 400 with a validation problem when it is the result's error
/// itself, unless an entry names its exact type. Deeper in the walk (a <see cref="ValidationFailed{TError}"/> as a
/// cause) only entries match, so a server error that wraps one stays a server error. Anything else answers
/// <see cref="DefaultStatusCode"/>, 500 by default.
/// </para>
/// <para>
/// A 4xx problem carries the matched error's <see cref="Error.Message"/> as <c>detail</c>, only when an entry (or the
/// built-in 400) matched it: an unmapped error answering a 4xx <see cref="DefaultStatusCode"/> has no <c>detail</c>.
/// A 5xx problem never carries the error's messages, validation messages included, outside the Development
/// environment; it is logged at Error instead, with <see cref="Error.ToString"/>.
/// </para>
/// </remarks>
public sealed class ResultfulHttpOptions
{
    private readonly Dictionary<Type, int> _map = [];
    private Func<Error, string> _validationKey = static error => ErrorWire.Name(error.GetType());
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
    /// The key of each entry in a validation problem's <c>errors</c>. Defaults to the error's wire name
    /// (<see cref="ErrorWire.Name"/>), the <c>type</c> JSON writes: <c>NotFound&lt;Guid&gt;</c> for a generic leaf.
    /// Errors with the same key are listed together, in order. A null key is treated as an empty one.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public Func<Error, string> ValidationKey
    {
        get => _validationKey;
        set => _validationKey = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The status for an error no entry maps. 500 by default; from 400 to 599. A 4xx default has no <c>detail</c>:
    /// only a mapped error shows its message. A 5xx default follows the 5xx rule.
    /// </summary>
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
