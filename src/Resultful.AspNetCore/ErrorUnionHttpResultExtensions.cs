namespace Resultful.AspNetCore;

/// <summary>
/// Turns a result whose error is an error union (an <see cref="IErrorUnion"/>) into a
/// <see cref="ResultHttpResult{T, TError}"/>. The union's leaf, <see cref="IErrorUnion.Error"/>, is what the map sees.
/// </summary>
/// <remarks>
/// A separate class from <see cref="HttpResultExtensions"/>: the two <c>ToHttpResult&lt;T, TError&gt;</c> overloads
/// differ only by constraint, and the compiler picks the one whose constraint the error type meets.
/// </remarks>
public static class ErrorUnionHttpResultExtensions
{
    /// <summary>A success answers 200 with its value; a failure, the mapped problem for the union's leaf.</summary>
    /// <typeparam name="T">The success value's type.</typeparam>
    /// <typeparam name="TError">The error union.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The HTTP result, typed with <see cref="Error"/>: the leaf may be any of the union's cases.</returns>
    /// <remarks>A zero (<c>default</c>) result has no case: converting it throws, as reading it does.</remarks>
    public static ResultHttpResult<T, Error> ToHttpResult<T, TError>(this Result<T, TError> result) where TError : struct, IErrorUnion =>
        result.TryGetValue(out var value) ? new(value) : new(result.Error.Error);
}
