namespace Resultful.AspNetCore;

/// <summary>
/// Turns a result into a <see cref="ResultHttpResult{T, TError}"/>. A result whose error is an error union has its own
/// overload, in <see cref="ErrorUnionHttpResultExtensions"/>.
/// </summary>
/// <remarks>A zero (<c>default</c>) result has no case: converting it throws, as reading it does.</remarks>
public static class HttpResultExtensions
{
    /// <summary>A success answers 200 with its value; a failure, the mapped problem for its error.</summary>
    /// <typeparam name="T">The success value's type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The HTTP result.</returns>
    public static ResultHttpResult<T, Error> ToHttpResult<T>(this Result<T> result) =>
        result.TryGetValue(out var value) ? new(value) : new(result.Error);

    /// <summary>A success answers 200 with its value; a failure, the mapped problem for its error.</summary>
    /// <typeparam name="T">The success value's type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The HTTP result.</returns>
    public static ResultHttpResult<T, TError> ToHttpResult<T, TError>(this Result<T, TError> result) where TError : Error =>
        result.TryGetValue(out var value) ? new(value) : new(result.Error);

    /// <summary>A success answers 200 with its value; a failure, 400 with a validation problem listing every error.</summary>
    /// <typeparam name="T">The success value's type.</typeparam>
    /// <param name="result">The validation.</param>
    /// <returns>The HTTP result.</returns>
    public static ResultHttpResult<T, ValidationFailed<Error>> ToHttpResult<T>(this Validation<T> result) =>
        result.TryGetValue(out var value) ? new(value) : new(new ValidationFailed<Error>(result.Errors));

    /// <summary>A success answers 200 with its value; a failure, 400 with a validation problem listing every error.</summary>
    /// <typeparam name="T">The success value's type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="result">The validation.</param>
    /// <returns>The HTTP result.</returns>
    public static ResultHttpResult<T, ValidationFailed<TError>> ToHttpResult<T, TError>(this Validation<T, TError> result) where TError : Error =>
        result.TryGetValue(out var value) ? new(value) : new(new ValidationFailed<TError>(result.Errors));
}
