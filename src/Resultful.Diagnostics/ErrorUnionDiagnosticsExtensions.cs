using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics;

/// <summary>
/// The hooks for a result whose error is an error union (an <see cref="IErrorUnion"/>, decision 17). They log and tag
/// the union's leaf, <see cref="IErrorUnion.Error"/>, exactly as <see cref="ResultLoggingExtensions"/> and
/// <see cref="ResultActivityExtensions"/> do for an error.
/// </summary>
/// <remarks>
/// A separate class: these overloads differ from the <c>where TError : Error</c> ones only by constraint, and the
/// compiler picks the one whose constraint the error type meets.
/// </remarks>
public static class ErrorUnionDiagnosticsExtensions
{
    /// <summary>Logs the union's leaf at <paramref name="level"/> if the result is a failure.</summary>
    /// <param name="result">The result.</param>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The level; <see cref="LogLevel.None"/> never logs.</param>
    /// <returns>The same result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public static Result<T, TError> LogFailure<T, TError>(this Result<T, TError> result, ILogger logger, LogLevel level)
        where TError : struct, IErrorUnion
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (result.IsFailure) ResultLoggingExtensions.Log(logger, level, result.Error.Error);
        return result;
    }

    /// <summary>Records the union's leaf on <paramref name="activity"/>, or on <see cref="Activity.Current"/> when it is null.</summary>
    /// <param name="result">The result.</param>
    /// <param name="activity">The activity to mark. Null means <see cref="Activity.Current"/>, which may be the HTTP server span: pass your own for an expected failure.</param>
    /// <returns>The same result.</returns>
    public static Result<T, TError> RecordFailure<T, TError>(this Result<T, TError> result, Activity? activity)
        where TError : struct, IErrorUnion
    {
        if (result.IsFailure) ResultActivityExtensions.Record(activity ?? Activity.Current, result.Error.Error);
        return result;
    }
}
