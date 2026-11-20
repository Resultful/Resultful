using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics;

/// <summary>
/// Logs a failure when a call asks for it. Each method logs one entry for a failure, with the error's leaf type name,
/// <see cref="Error.Message"/>, <see cref="Error.Context"/> and the type and message of each cause, never the leaf's
/// own properties. A success, and the zero value, log nothing. Each returns the result unchanged, so it sits in a chain.
/// </summary>
/// <remarks>A result whose error is a union (decision 17) widens first: <c>result.AsResult().LogFailure(…)</c>.</remarks>
public static class ResultLoggingExtensions
{
    private static readonly EventId FailureEvent = new(1, "ResultFailure");

    /// <summary>Logs the result's error at <paramref name="level"/> if it is a failure.</summary>
    /// <returns>The same result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public static Result<T> LogFailure<T>(this Result<T> result, ILogger logger, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (result.IsFailure) Log(logger, level, result.Error);
        return result;
    }

    /// <summary>Logs the result's error at <paramref name="level"/> if it is a failure.</summary>
    /// <returns>The same result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public static Result<T, TError> LogFailure<T, TError>(this Result<T, TError> result, ILogger logger, LogLevel level)
        where TError : Error
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (result.IsFailure) Log(logger, level, result.Error);
        return result;
    }

    /// <summary>
    /// Logs a failed validation's errors at <paramref name="level"/> as one entry: a <c>ValidationFailed&lt;Error&gt;</c>
    /// whose causes are the errors, in order.
    /// </summary>
    /// <returns>The same validation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public static Validation<T> LogFailure<T>(this Validation<T> result, ILogger logger, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (result.IsFailure && level != LogLevel.None && logger.IsEnabled(level))
            logger.Log(level, FailureEvent, new FailureLogState(new ValidationFailed<Error>(result.Errors)), null, FailureLogState.Render);
        return result;
    }

    private static void Log(ILogger logger, LogLevel level, Error error)
    {
        if (level != LogLevel.None && logger.IsEnabled(level))
            logger.Log(level, FailureEvent, new FailureLogState(error), null, FailureLogState.Render);
    }
}
